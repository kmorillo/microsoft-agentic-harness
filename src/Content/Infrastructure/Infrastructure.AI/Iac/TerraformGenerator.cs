using Application.AI.Common.Interfaces.Iac;
using Application.AI.Common.Interfaces.Sandbox;
using Domain.AI.Iac;
using Domain.AI.Sandbox;
using Domain.Common;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Iac;

/// <summary>
/// Terraform <see cref="IIacGenerator"/>. Scaffolds a starter module
/// deterministically, validates + plans it with the <c>terraform</c> CLI, and
/// security-scans it with Checkov + tfsec — all CLI work runs inside the PR-3
/// sandbox via <see cref="IacSandboxRunner"/>. Never deploys: there is no apply.
/// </summary>
/// <remarks>
/// <para>
/// Stable failure codes (<c>iac.*</c>): the implementation never surfaces raw CLI
/// stderr in a <see cref="Result"/> error — it logs the full output via structured
/// logging and returns a scrubbed code so a SAS token or registry credential in a
/// provider error can never leak into LLM context or an audit line.
/// </para>
/// </remarks>
public sealed class TerraformGenerator : IIacGenerator
{
    private const string CliProgram = "terraform";
    private const string CheckovProgram = "checkov";
    private const string TfsecProgram = "tfsec";

    private readonly IOptionsMonitor<AppConfig> _config;
    private readonly ISandboxExecutor _sandbox;
    private readonly ILogger<TerraformGenerator> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new <see cref="TerraformGenerator"/>.</summary>
    /// <param name="config">Application configuration monitor — supplies version pins, registry allowlist, and blocking severity.</param>
    /// <param name="sandbox">The Process-isolation sandbox executor the CLIs run inside.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="timeProvider">Clock abstraction (unused for timing here but injected for parity and future use).</param>
    public TerraformGenerator(
        IOptionsMonitor<AppConfig> config,
        ISandboxExecutor sandbox,
        ILogger<TerraformGenerator> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _config = config;
        _sandbox = sandbox;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public IacBackend Backend => IacBackend.Terraform;

    /// <inheritdoc />
    public Task<Result<IacGenerationResult>> GenerateAsync(
        IacGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ResourceType) || string.IsNullOrWhiteSpace(request.ResourceName))
        {
            return Task.FromResult(Result<IacGenerationResult>.Fail("iac.generate.invalid_request"));
        }

        var files = new Dictionary<string, string>
        {
            ["main.tf"] = BuildMainTf(request),
            ["variables.tf"] = BuildVariablesTf(request)
        };

        return Task.FromResult(Result<IacGenerationResult>.Success(new IacGenerationResult
        {
            Backend = IacBackend.Terraform,
            Files = files
        }));
    }

    /// <inheritdoc />
    public async Task<Result<IacPlanResult>> PlanAsync(string moduleDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(moduleDirectory))
        {
            return Result<IacPlanResult>.Fail("iac.plan.invalid_module_directory");
        }

        var allowlist = _config.CurrentValue.AI.Iac.RegistryAllowlist;

        var validate = await Run([ "validate", "-no-color" ], moduleDirectory, allowlist, "iac_plan", cancellationToken);
        if (validate is null)
        {
            return Result<IacPlanResult>.Fail("iac.plan.sandbox_error");
        }

        if (!validate.Success)
        {
            _logger.LogWarning("Terraform validate failed in {Module}: exit={Exit}", moduleDirectory, validate.ExitCode);
            return Result<IacPlanResult>.Success(FailedPlan(moduleDirectory, validate.Output ?? string.Empty, "validate failed"));
        }

        var plan = await Run([ "plan", "-no-color", "-detailed-exitcode" ], moduleDirectory, allowlist, "iac_plan", cancellationToken);
        if (plan is null)
        {
            return Result<IacPlanResult>.Fail("iac.plan.sandbox_error");
        }

        return Result<IacPlanResult>.Success(ParsePlan(moduleDirectory, plan));
    }

    /// <inheritdoc />
    public async Task<Result<IacScanResult>> ScanAsync(string moduleDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(moduleDirectory))
        {
            return Result<IacScanResult>.Fail("iac.scan.invalid_module_directory");
        }

        var iac = _config.CurrentValue.AI.Iac;
        if (!IacScanSeverityParser.TryParse(iac.BlockingSeverity, out var blocking))
        {
            return Result<IacScanResult>.Fail("iac.scan.invalid_blocking_severity");
        }

        var checkov = await Run([ "-d", ".", "--compact", "--quiet" ], moduleDirectory, iac.RegistryAllowlist, "iac_scan", cancellationToken, CheckovProgram);
        var tfsec = await Run([ ".", "--no-colour" ], moduleDirectory, iac.RegistryAllowlist, "iac_scan", cancellationToken, TfsecProgram);
        if (checkov is null || tfsec is null)
        {
            return Result<IacScanResult>.Fail("iac.scan.sandbox_error");
        }

        var findings = new List<IacScanFinding>();
        findings.AddRange(CheckovParser.Parse(checkov.Output ?? string.Empty));
        findings.AddRange(TfsecParser.Parse(tfsec.Output ?? string.Empty));

        return Result<IacScanResult>.Success(new IacScanResult
        {
            Backend = IacBackend.Terraform,
            ModulePath = moduleDirectory,
            Passed = IacScanSeverityParser.Passes(findings, blocking),
            ScannersRun = [CheckovProgram, TfsecProgram],
            Findings = findings
        });
    }

    private Task<SandboxExecutionResult?> Run(
        IReadOnlyList<string> args,
        string moduleDirectory,
        IReadOnlyList<string> allowlist,
        string toolName,
        CancellationToken cancellationToken,
        string program = CliProgram)
        => RunGuarded(program, args, moduleDirectory, allowlist, toolName, cancellationToken);

    private async Task<SandboxExecutionResult?> RunGuarded(
        string program,
        IReadOnlyList<string> args,
        string moduleDirectory,
        IReadOnlyList<string> allowlist,
        string toolName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await IacSandboxRunner.RunAsync(program, args, moduleDirectory, allowlist, _sandbox, toolName, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Terraform sandbox run failed for {Program} in {Module}.", program, moduleDirectory);
            return null;
        }
    }

    private static IacPlanResult ParsePlan(string moduleDirectory, SandboxExecutionResult plan)
    {
        // terraform plan -detailed-exitcode: 0 = no changes, 2 = changes present, 1 = error.
        var output = plan.Output ?? string.Empty;
        var hasChanges = plan.ExitCode == 2 || output.Contains("to add", StringComparison.OrdinalIgnoreCase);
        var destructive = output.Contains("to destroy", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("0 to destroy", StringComparison.OrdinalIgnoreCase);
        var succeeded = plan.ExitCode is 0 or 2;

        return new IacPlanResult
        {
            Backend = IacBackend.Terraform,
            ModulePath = moduleDirectory,
            Succeeded = succeeded,
            HasChanges = hasChanges,
            HasDestructiveChanges = destructive,
            RawOutput = output,
            Summary = succeeded ? PlanSummaryLine(output) : "plan errored"
        };
    }

    private static IacPlanResult FailedPlan(string moduleDirectory, string output, string summary) => new()
    {
        Backend = IacBackend.Terraform,
        ModulePath = moduleDirectory,
        Succeeded = false,
        HasChanges = false,
        HasDestructiveChanges = false,
        RawOutput = output,
        Summary = summary
    };

    private static string PlanSummaryLine(string output)
    {
        var line = output.Split('\n').LastOrDefault(l => l.Contains("to add", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(line) ? "no changes" : line.Trim();
    }

    private static string BuildMainTf(IacGenerationRequest request)
    {
        var attributes = string.Join(
            "\n",
            request.Parameters.Select(p => $"  {p.Key} = \"{p.Value}\""));
        var body = string.IsNullOrEmpty(attributes) ? string.Empty : attributes + "\n";

        return $$"""
            # Scaffolded by the Microsoft Agentic Harness IaC skill (Terraform).
            # Environment: {{request.Environment}}
            resource "{{request.ResourceType}}" "{{request.ResourceName}}" {
            {{body}}  tags = {
                environment = var.environment
                managed_by  = "agentic-harness"
              }
            }
            """;
    }

    private static string BuildVariablesTf(IacGenerationRequest request) => $$"""
        variable "environment" {
          description = "Target environment for the {{request.ResourceName}} resource."
          type        = string
          default     = "{{request.Environment}}"
        }
        """;
}
