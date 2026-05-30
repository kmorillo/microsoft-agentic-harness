using System.Globalization;
using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.Core.CQRS.Evaluation.RunEvalSuite;
using Domain.AI.Evaluation;
using Infrastructure.AI.Evaluation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Presentation.Common.Extensions;

namespace Presentation.EvalRunner;

/// <summary>
/// Entry point for the offline evaluation CLI.
/// </summary>
/// <remarks>
/// <para><b>Usage:</b></para>
/// <code>
/// evalrun &lt;dataset.yaml&gt; [&lt;dataset2.yaml&gt; ...]
///         [--out console|json|junit]   # output format (default: console)
///         [--out-file &lt;path&gt;]     # write to file instead of stdout
///         [--repeats N]                # 1..50 (default 1; CI default 3)
///         [--parallel N]               # cases run concurrently (default 1)
///         [--tags tag1,tag2]           # only run cases with at least one matching tag
///         [--fail-rate F]              # 0.0..1.0 acceptable failure fraction (default 0)
///         [--deterministic]            # force temperature=0 on every invocation
/// </code>
/// <para>
/// Exit code 0 when the overall verdict is <see cref="Verdict.Pass"/>, 1 otherwise.
/// Validation failures (bad args, missing files) exit 2.
/// </para>
/// </remarks>
public static class Program
{
    private static readonly IReadOnlySet<string> KnownOutputFormats =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "console", "json", "junit" };

    private const int RepeatsCostWarningThreshold = 10;

    /// <summary>Entry point.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        CliArgs parsed;
        try
        {
            parsed = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Invalid arguments: {ex.Message}");
            PrintUsage();
            return 2;
        }

        if (parsed.DatasetPaths.Count == 0)
        {
            Console.Error.WriteLine("At least one dataset path is required.");
            PrintUsage();
            return 2;
        }

        if (parsed.Repeats > RepeatsCostWarningThreshold)
        {
            Console.Error.WriteLine(
                $"Warning: --repeats {parsed.Repeats} multiplies LLM-judge cost by {parsed.Repeats}x per case. " +
                $"Consider {RepeatsCostWarningThreshold} or below unless stability variance demands more.");
        }

        // Wire up SIGINT / Ctrl+C → cancellation across the whole run so a stuck judge
        // or long-running case can be aborted cleanly without truncating output files.
        // The handler is guarded against ObjectDisposedException because Console.CancelKeyPress
        // is a process-global static event and may fire during shutdown after the cts is
        // disposed; we also explicitly detach the handler in finally to avoid the race.
        var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            try
            {
                eventArgs.Cancel = true; // suppress immediate process exit; let the runner clean up
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // cts was already disposed (normal-exit race) — nothing to cancel.
            }
        };
        Console.CancelKeyPress += cancelHandler;

        var services = new ServiceCollection();
        services.GetServices(includeHealthChecksUI: false);
        services.AddEvaluationDependencies();

        await using var provider = services.BuildServiceProvider();

        // Hosted services tracked for orderly shutdown. We keep a SEPARATE list of services
        // that we successfully started so a mid-startup failure on service N doesn't leak
        // services 1..N-1 (and so we never call StopAsync on a service that never started).
        var allHostedServices = provider.GetServices<IHostedService>().ToList();
        var startedHostedServices = new List<IHostedService>(allHostedServices.Count);

        // Bound the shutdown wait so a hostile/hung hosted service can't pin the process
        // forever during SIGINT recovery or normal teardown.
        var shutdownTimeout = TimeSpan.FromSeconds(15);

        try
        {
            // Start hosted services (skill seeding, planner DB migration, governance bootstrap,
            // drift baseline loader, etc.) — the harness expects these to run before any
            // ExecuteAgentTurnCommand is dispatched. ConsoleUI/Program.cs follows the same pattern.
            foreach (var hostedService in allHostedServices)
            {
                await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
                startedHostedServices.Add(hostedService);
            }

            var mediator = provider.GetRequiredService<IMediator>();

            var command = new RunEvalSuiteCommand
            {
                DatasetPaths = parsed.DatasetPaths,
                Options = new EvalRunOptions
                {
                    Repeats = parsed.Repeats,
                    Parallelism = parsed.Parallelism,
                    TagFilter = parsed.Tags,
                    FailRateThreshold = parsed.FailRateThreshold,
                    ForceDeterministic = parsed.Deterministic
                }
            };

            var result = await mediator.Send(command, cts.Token);
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine("Eval run failed:");
                foreach (var err in result.Errors) Console.Error.WriteLine($"  - {err}");
                return 2;
            }

            var report = result.Value!;
            var reporter = SelectReporter(provider, parsed.OutputFormat);

            if (parsed.OutputFilePath is { } outFile)
            {
                var dir = Path.GetDirectoryName(outFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(outFile))
                {
                    Console.Error.WriteLine($"Overwriting existing file: {outFile}");
                }

                await using var fs = File.Create(outFile);
                await reporter.WriteAsync(report, fs, cts.Token);
            }
            else
            {
                await using var stdout = Console.OpenStandardOutput();
                await reporter.WriteAsync(report, stdout, cts.Token);
            }

            return report.OverallVerdict == Verdict.Pass ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Eval run cancelled.");
            return 130; // conventional exit code for SIGINT
        }
        finally
        {
            // Stop only services we actually started, with a bounded timeout so a misbehaving
            // hosted service can't keep the process alive past the shutdown budget.
            using var stopCts = new CancellationTokenSource(shutdownTimeout);
            foreach (var hostedService in startedHostedServices)
            {
                try { await hostedService.StopAsync(stopCts.Token).ConfigureAwait(false); }
                catch { /* best-effort; we're shutting down */ }
            }

            // Detach the SIGINT handler BEFORE disposing the cts so the closure can no longer
            // observe (and dereference) a disposed cts.
            Console.CancelKeyPress -= cancelHandler;
            cts.Dispose();
        }
    }

    private static IEvalReporter SelectReporter(IServiceProvider provider, string formatKey)
    {
        var reporters = provider.GetServices<IEvalReporter>().ToList();
        var match = reporters.FirstOrDefault(r =>
            string.Equals(r.FormatKey, formatKey, StringComparison.OrdinalIgnoreCase));
        return match
            ?? throw new InvalidOperationException(
                $"No IEvalReporter registered for format '{formatKey}'. " +
                $"Available: {string.Join(", ", reporters.Select(r => r.FormatKey))}");
    }

    private static CliArgs ParseArgs(string[] args)
    {
        var datasets = new List<string>();
        string outFormat = "console";
        string? outFile = null;
        int repeats = 1;
        int parallelism = 1;
        double failRate = 0.0;
        bool deterministic = false;
        IReadOnlyList<string>? tags = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--out":
                    outFormat = RequireValue(args, ref i, "--out");
                    break;
                case "--out-file":
                    outFile = RequireValue(args, ref i, "--out-file");
                    break;
                case "--repeats":
                    repeats = ParsePositiveInt(RequireValue(args, ref i, "--repeats"), 1, 50, "--repeats");
                    break;
                case "--parallel":
                    parallelism = ParsePositiveInt(RequireValue(args, ref i, "--parallel"), 1, 128, "--parallel");
                    break;
                case "--fail-rate":
                    failRate = ParseFraction(RequireValue(args, ref i, "--fail-rate"));
                    break;
                case "--deterministic":
                    deterministic = true;
                    break;
                case "--tags":
                    tags = RequireValue(args, ref i, "--tags")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown flag '{a}'.");
                    datasets.Add(a);
                    break;
            }
        }

        if (!KnownOutputFormats.Contains(outFormat))
        {
            throw new ArgumentException(
                $"Unknown --out format '{outFormat}'. Known: {string.Join(", ", KnownOutputFormats)}.");
        }

        return new CliArgs(datasets, outFormat, outFile, repeats, parallelism, failRate, deterministic, tags);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Flag '{flag}' requires a value.");
        return args[++i];
    }

    private static int ParsePositiveInt(string raw, int min, int max, string flag)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
            throw new ArgumentException($"'{flag}' must be an integer in [{min}, {max}], got '{raw}'.");
        return v;
    }

    private static double ParseFraction(string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 1)
            throw new ArgumentException($"'--fail-rate' must be in [0.0, 1.0], got '{raw}'.");
        return v;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: evalrun <dataset.yaml> [...] [options]

            Options:
              --out FORMAT          console (default) | json | junit
              --out-file PATH       write report to file instead of stdout
              --repeats N           1..50 (default 1; CI default 3); warns above 10 due to cost
              --parallel N          concurrent cases (default 1)
              --tags tag1,tag2      only run cases matching at least one tag
              --fail-rate F         max failed-case fraction for overall Pass (default 0.0)
              --deterministic       force temperature=0 on every invocation

            Exit codes:
              0   — overall verdict Pass
              1   — overall verdict Fail / Warn
              2   — argument or load error
              130 — cancelled (SIGINT / Ctrl+C)
            """);
    }

    private sealed record CliArgs(
        IReadOnlyList<string> DatasetPaths,
        string OutputFormat,
        string? OutputFilePath,
        int Repeats,
        int Parallelism,
        double FailRateThreshold,
        bool Deterministic,
        IReadOnlyList<string>? Tags);
}
