namespace Domain.AI.Evaluation;

/// <summary>
/// The aggregated outcome of running one or more <see cref="EvalDataset"/>s through
/// the eval framework. Consumed by reporters (console, JSON, JUnit XML) and by
/// the dashboard (via JSON ingestion).
/// </summary>
/// <remarks>
/// One report covers one run. A run may cover multiple datasets (e.g. CI runs the
/// entire <c>eval-datasets/seed/*.yaml</c> set in a single invocation).
/// </remarks>
public sealed record EvalRunReport
{
    /// <summary>Stable identifier for this run, generated at start-of-run.</summary>
    public required string RunId { get; init; }

    /// <summary>UTC timestamp when the run started.</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>UTC timestamp when the run completed (start + <see cref="Duration"/>).</summary>
    public required DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>Total wall-clock duration of the run.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>The datasets evaluated in this run.</summary>
    public required IReadOnlyList<EvalDataset> Datasets { get; init; }

    /// <summary>All case results across all datasets, in invocation order.</summary>
    public required IReadOnlyList<EvalResult> Results { get; init; }

    /// <summary>Number of cases with overall Verdict.Pass.</summary>
    public int PassedCount { get; init; }

    /// <summary>Number of cases with overall Verdict.Fail.</summary>
    public int FailedCount { get; init; }

    /// <summary>Number of cases with overall Verdict.Warn.</summary>
    public int WarnedCount { get; init; }

    /// <summary>Number of cases that errored during execution (could not be scored).</summary>
    public int ErroredCount { get; init; }

    /// <summary>Cumulative cost in USD across all cases, repeats, and metrics.</summary>
    public decimal TotalCostUsd { get; init; }

    /// <summary>The repeats configuration used for this run. Surfaced in reports for traceability.</summary>
    public int Repeats { get; init; } = 1;

    /// <summary>
    /// The overall verdict for the run, derived from per-case results and
    /// the run's fail-rate threshold. Used by CI to determine exit code.
    /// </summary>
    public required Verdict OverallVerdict { get; init; }

    /// <summary>
    /// The pass rate across cases (PassedCount / total non-errored cases), in 0.0–1.0.
    /// </summary>
    public double PassRate => (PassedCount + FailedCount + WarnedCount) == 0
        ? 0.0
        : (double)PassedCount / (PassedCount + FailedCount + WarnedCount);
}
