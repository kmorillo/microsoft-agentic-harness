namespace Application.AI.Common.Evaluation.Models;

/// <summary>
/// Options controlling how an evaluation run executes.
/// </summary>
/// <remarks>
/// <para>
/// Defaults favor fast local-developer iteration: single repeat, sequential execution,
/// no failure threshold (warning only). CI invocations override to <c>Repeats=3</c>
/// for stability and a tighter <see cref="FailRateThreshold"/> for gating.
/// </para>
/// </remarks>
public sealed record EvalRunOptions
{
    /// <summary>
    /// Number of times each case is re-invoked, with median-across-repeats aggregation.
    /// Smooths out noise from non-deterministic LLM judges and sampling temperature.
    /// Must be between 1 and 50.
    /// </summary>
    public int Repeats { get; init; } = 1;

    /// <summary>
    /// Max number of cases to invoke in parallel. Bounded by <c>SemaphoreSlim</c>.
    /// Tune to match provider rate limits.
    /// </summary>
    public int Parallelism { get; init; } = 1;

    /// <summary>
    /// Optional tag filter. If non-empty, only cases whose <c>Tags</c> intersect this
    /// set are executed. Matching is case-insensitive.
    /// </summary>
    public IReadOnlyList<string>? TagFilter { get; init; }

    /// <summary>
    /// Maximum acceptable fraction of failed cases for the run's overall verdict to be Pass.
    /// 0.0 = any failure is a run-level failure (strict). 1.0 = always pass at the run level.
    /// </summary>
    public double FailRateThreshold { get; init; } = 0.0;

    /// <summary>Optional invocation overrides (e.g. temperature) applied to every case in the run.</summary>
    public IReadOnlyDictionary<string, string>? InvocationOverrides { get; init; }

    /// <summary>Whether to force <c>temperature = 0</c> on every invocation (used in replay scenarios).</summary>
    public bool ForceDeterministic { get; init; }
}
