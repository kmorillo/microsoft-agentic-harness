namespace Domain.AI.Evaluation;

/// <summary>
/// The outcome of running one <see cref="EvalCase"/> through the harness and scoring it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OutputPerRepeat"/> captures the raw harness output for each repeat invocation
/// (length = N when <c>Repeats=N</c>). <see cref="ScoresPerRepeat"/> captures the metric scores
/// for each repeat. The aggregated <see cref="Verdict"/> and <see cref="AggregatedScores"/>
/// reflect median-across-repeats per metric, with the case's overall verdict being the
/// worst per-metric verdict (Fail > Warn > Pass).
/// </para>
/// </remarks>
public sealed record EvalResult
{
    /// <summary>The case this result corresponds to.</summary>
    public required EvalCase Case { get; init; }

    /// <summary>The harness output for each repeat invocation. Length equals the configured Repeats.</summary>
    public required IReadOnlyList<string> OutputPerRepeat { get; init; }

    /// <summary>The per-metric scores for each repeat. Outer list = repeats, inner list = metrics.</summary>
    public required IReadOnlyList<IReadOnlyList<MetricScore>> ScoresPerRepeat { get; init; }

    /// <summary>
    /// The aggregated (median across repeats) score per metric key.
    /// Provides a stable summary even when individual judge runs are noisy.
    /// </summary>
    public required IReadOnlyDictionary<string, MetricScore> AggregatedScores { get; init; }

    /// <summary>The overall verdict for this case (worst per-metric verdict).</summary>
    public required Verdict Verdict { get; init; }

    /// <summary>Cumulative cost in USD across all repeats and metrics for this case.</summary>
    public decimal CostUsd { get; init; }

    /// <summary>Total wall-clock duration for this case (sum across repeats).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Optional error string when the case failed to execute (not the same as a metric Fail).</summary>
    public string? Error { get; init; }
}
