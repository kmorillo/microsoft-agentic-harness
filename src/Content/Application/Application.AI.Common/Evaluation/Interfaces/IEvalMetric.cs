using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;

namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Scores a single evaluation case's output. Implementations are registered as
/// keyed services so cases can reference them by <see cref="Key"/> in YAML/JSON.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be safe to invoke concurrently — the runner may score
/// many cases in parallel against the same metric instance.
/// </para>
/// <para>
/// Implementations should fail soft: when a metric cannot produce a confident score
/// (e.g. an LLM judge returns malformed JSON), return a <see cref="MetricScore"/> with
/// <see cref="Verdict.Warn"/> rather than throwing. Exceptions bubble to the runner
/// and mark the case as Errored, which is a heavier failure mode than Warn.
/// </para>
/// </remarks>
public interface IEvalMetric
{
    /// <summary>The stable string key by which this metric is referenced from cases (e.g. "exact_match").</summary>
    string Key { get; }

    /// <summary>
    /// Scores the given case's output.
    /// </summary>
    /// <param name="case">The case being evaluated. Provides expected output, context, etc.</param>
    /// <param name="output">The harness output for this case.</param>
    /// <param name="spec">The metric specification from the case (threshold, parameters).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The score and verdict. Never null.</returns>
    Task<MetricScore> ScoreAsync(
        EvalCase @case,
        AgentInvocationResult output,
        MetricSpec spec,
        CancellationToken cancellationToken);
}
