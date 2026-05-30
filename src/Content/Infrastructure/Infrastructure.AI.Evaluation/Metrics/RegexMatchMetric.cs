using System.Diagnostics;
using System.Text.RegularExpressions;
using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;

namespace Infrastructure.AI.Evaluation.Metrics;

/// <summary>
/// Scores 1.0 when the agent's output matches the regex in the spec's <c>pattern</c>
/// parameter, else 0.0. Uses <see cref="RegexOptions.None"/> by default with a 1-second
/// timeout to defend against catastrophic backtracking.
/// </summary>
public sealed class RegexMatchMetric : IEvalMetric
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public string Key => "regex_match";

    /// <inheritdoc />
    public Task<MetricScore> ScoreAsync(
        EvalCase @case,
        AgentInvocationResult output,
        MetricSpec spec,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        if (!spec.Parameters.TryGetValue("pattern", out var pattern) || string.IsNullOrWhiteSpace(pattern))
        {
            sw.Stop();
            return Task.FromResult(Warn(sw, "Missing required 'pattern' parameter."));
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            sw.Stop();
            return Task.FromResult(Warn(sw, $"Invalid regex pattern: {ex.Message}"));
        }

        bool isMatch;
        try
        {
            isMatch = regex.IsMatch(output.Output);
        }
        catch (RegexMatchTimeoutException)
        {
            sw.Stop();
            return Task.FromResult(Warn(sw, "Regex evaluation timed out (possible catastrophic backtracking)."));
        }

        sw.Stop();
        return Task.FromResult(new MetricScore
        {
            MetricKey = Key,
            Score = isMatch ? 1.0 : 0.0,
            Verdict = isMatch ? Verdict.Pass : Verdict.Fail,
            Reasoning = isMatch
                ? $"Output matched pattern: {pattern}"
                : $"Output did not match pattern: {pattern}",
            Duration = sw.Elapsed
        });
    }

    private MetricScore Warn(Stopwatch sw, string reason) => new()
    {
        MetricKey = Key,
        Score = 0.0,
        Verdict = Verdict.Warn,
        Reasoning = reason,
        Duration = sw.Elapsed
    };
}
