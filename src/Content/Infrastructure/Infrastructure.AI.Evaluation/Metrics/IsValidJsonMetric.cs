using System.Diagnostics;
using System.Text.Json;
using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;

namespace Infrastructure.AI.Evaluation.Metrics;

/// <summary>
/// Scores 1.0 when the agent's output parses as valid JSON, else 0.0.
/// </summary>
/// <remarks>
/// Useful as a baseline guardrail for agents that must return structured output. Full JSON
/// Schema validation (against a declared schema) is intentionally out of scope here — that
/// requires an extra dependency (<c>NJsonSchema</c> or <c>JsonSchema.Net</c>) which is
/// deferred until a consumer actually needs it.
/// </remarks>
public sealed class IsValidJsonMetric : IEvalMetric
{
    /// <inheritdoc />
    public string Key => "is_valid_json";

    /// <inheritdoc />
    public Task<MetricScore> ScoreAsync(
        EvalCase @case,
        AgentInvocationResult output,
        MetricSpec spec,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        bool isValid;
        string reasoning;

        if (string.IsNullOrWhiteSpace(output.Output))
        {
            isValid = false;
            reasoning = "Output was empty.";
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(output.Output);
                isValid = true;
                reasoning = "Output parsed as valid JSON.";
            }
            catch (JsonException ex)
            {
                isValid = false;
                reasoning = $"JSON parse failed: {ex.Message}";
            }
        }

        sw.Stop();
        return Task.FromResult(new MetricScore
        {
            MetricKey = Key,
            Score = isValid ? 1.0 : 0.0,
            Verdict = isValid ? Verdict.Pass : Verdict.Fail,
            Reasoning = reasoning,
            Duration = sw.Elapsed
        });
    }
}
