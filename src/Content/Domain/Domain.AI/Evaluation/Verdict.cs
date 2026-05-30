namespace Domain.AI.Evaluation;

/// <summary>
/// The outcome of an evaluation case against a metric or aggregated across metrics.
/// </summary>
/// <remarks>
/// Used by both individual <see cref="MetricScore"/> outcomes and aggregated
/// <see cref="EvalResult"/> outcomes. The semantics:
/// <list type="bullet">
///   <item><description><see cref="Pass"/> — score met or exceeded threshold.</description></item>
///   <item><description><see cref="Fail"/> — score below threshold (hard failure, counts toward CI gate).</description></item>
///   <item><description><see cref="Warn"/> — soft failure, e.g. judge produced malformed output. Does not count toward CI gate but surfaces in reports.</description></item>
/// </list>
/// </remarks>
public enum Verdict
{
    /// <summary>The metric or case passed.</summary>
    Pass = 0,

    /// <summary>The metric or case failed. Counts toward CI gate.</summary>
    Fail = 1,

    /// <summary>Soft failure (e.g. malformed judge output). Surfaced but not gating.</summary>
    Warn = 2
}
