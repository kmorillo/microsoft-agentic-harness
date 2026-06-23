namespace Domain.AI.SkillTraining;

/// <summary>
/// Immutable input bundle for the two-split non-regression gate
/// (<c>IGateEvaluator.EvaluateTwoSplit</c>). Carries both the held-out (validation) and held-in
/// (proposer-visible) score signals the policy compares on, plus the projection metric and the
/// running (current, best) lineage state.
/// </summary>
/// <remarks>
/// <para>
/// The held-out fields mirror the single-split <c>Evaluate</c> contract exactly. The held-in fields
/// (<see cref="CandidateHeldInHard"/>, <see cref="CandidateHeldInSoft"/>,
/// <see cref="CurrentHeldInScore"/>) are the additional signal the two-split rule needs; they are
/// the scores on the same task split the proposer reflected on.
/// </para>
/// <para>
/// As with the single-split gate, candidate scores are supplied as raw (hard, soft) pairs and
/// projected onto the comparison metric <em>inside</em> the evaluator, while the current/best scores
/// are supplied pre-projected. Re-projecting on every call (rather than persisting projected values)
/// keeps a candidate that ties an earlier accepted skill comparing bit-identically across checkpoint
/// round-trips.
/// </para>
/// </remarks>
public sealed record GateEvaluation
{
    /// <summary>The candidate skill document being evaluated.</summary>
    public required string CandidateSkill { get; init; }

    /// <summary>Candidate's aggregate hard (exact-match) score on the held-out split, in [0, 1].</summary>
    public required double CandidateHard { get; init; }

    /// <summary>Candidate's aggregate soft (graded) score on the held-out split, in [0, 1].</summary>
    public required double CandidateSoft { get; init; }

    /// <summary>Candidate's aggregate hard score on the held-in (proposer-visible) split, in [0, 1].</summary>
    public required double CandidateHeldInHard { get; init; }

    /// <summary>Candidate's aggregate soft score on the held-in split, in [0, 1].</summary>
    public required double CandidateHeldInSoft { get; init; }

    /// <summary>The currently-active skill at the moment of the comparison.</summary>
    public required string CurrentSkill { get; init; }

    /// <summary>Metric-projected held-out score of <see cref="CurrentSkill"/>, in [0, 1].</summary>
    public required double CurrentScore { get; init; }

    /// <summary>Metric-projected held-in score of <see cref="CurrentSkill"/>, in [0, 1].</summary>
    public required double CurrentHeldInScore { get; init; }

    /// <summary>The best-so-far skill across the run (tracked on the held-out metric).</summary>
    public required string BestSkill { get; init; }

    /// <summary>Metric-projected held-out score of <see cref="BestSkill"/>, in [0, 1].</summary>
    public required double BestScore { get; init; }

    /// <summary>Training step at which <see cref="BestSkill"/> was last updated.</summary>
    public required int BestStep { get; init; }

    /// <summary>Current training step (recorded when a new best is promoted).</summary>
    public required int GlobalStep { get; init; }

    /// <summary>Which metric both splits are projected onto. Defaults to <see cref="GateMetric.Hard"/>.</summary>
    public GateMetric Metric { get; init; } = GateMetric.Hard;

    /// <summary>
    /// Soft-weight when <see cref="Metric"/> is <see cref="GateMetric.Mixed"/>. Must be in [0, 1].
    /// Ignored otherwise. Defaults to 0.5.
    /// </summary>
    public double MixedWeight { get; init; } = 0.5;
}
