namespace Application.AI.Common.Evaluation.Models;

/// <summary>
/// Per-million-token rates used by <c>LlmJudgeMetric</c> to populate
/// <c>MetricScore.CostUsd</c> from <c>UsageDetails</c> token counts.
/// </summary>
/// <remarks>
/// <para>
/// Defaults are <c>0</c> so the metric reports <c>CostUsd = 0</c> until the consumer
/// wires real rates via <see cref="Microsoft.Extensions.Options.IOptions{T}"/>:
/// </para>
/// <code>
/// services.Configure&lt;JudgeCostOptions&gt;(o =&gt;
/// {
///     o.InputCostPerMillionTokens  = 5.00m;   // example GPT-4o input rate
///     o.OutputCostPerMillionTokens = 15.00m;  // example GPT-4o output rate
/// });
/// </code>
/// <para>
/// A future Sub-phase 5.4 model-rate table will supersede this with a
/// per-deployment lookup; this options block is the minimum viable plumbing
/// so cost surfaces today are not stuck at zero.
/// </para>
/// </remarks>
public sealed class JudgeCostOptions
{
    /// <summary>Cost in USD per 1,000,000 input tokens. Default 0 (no cost reported).</summary>
    public decimal InputCostPerMillionTokens { get; set; }

    /// <summary>Cost in USD per 1,000,000 output tokens. Default 0 (no cost reported).</summary>
    public decimal OutputCostPerMillionTokens { get; set; }

    /// <summary>
    /// Computes the USD cost of a single LLM call given input and output token counts.
    /// </summary>
    /// <param name="inputTokens">Tokens consumed by the prompt.</param>
    /// <param name="outputTokens">Tokens emitted by the model.</param>
    /// <returns>Computed cost in USD. Returns <c>0</c> when both rates are zero.</returns>
    public decimal Compute(long inputTokens, long outputTokens)
    {
        if (InputCostPerMillionTokens == 0m && OutputCostPerMillionTokens == 0m) return 0m;
        return (InputCostPerMillionTokens * inputTokens + OutputCostPerMillionTokens * outputTokens) / 1_000_000m;
    }
}
