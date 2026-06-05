using Domain.AI.SkillTraining;
using Domain.Common;
using MediatR;

namespace Application.AI.Common.CQRS.SkillTraining.GateCandidateSkill;

/// <summary>
/// Evaluates whether a candidate skill, scored on a selection set, should be accepted,
/// promoted to new best, or rejected.
/// </summary>
/// <remarks>
/// <para>
/// This command is the CQRS surface for SkillOpt's validation gate. It is a pure decision
/// command: the caller (the training orchestrator in Phase 4) is responsible for actually
/// rolling out the candidate skill and computing <see cref="CandidateHard"/> /
/// <see cref="CandidateSoft"/> beforehand. Keeping rollout out of the gate lets callers
/// cache results by skill hash and run the gate in tight loops without re-engaging the
/// agent stack.
/// </para>
/// <para>
/// Strict greater-than semantics: a candidate that ties the current score is rejected.
/// This avoids thrashing across statistically equivalent skills.
/// </para>
/// </remarks>
public sealed record GateCandidateSkillCommand : IRequest<Result<GateResult>>
{
    /// <summary>The candidate skill document being evaluated.</summary>
    public required string CandidateSkill { get; init; }

    /// <summary>Aggregate hard (binary / exact-match) score of the candidate in [0, 1].</summary>
    public required double CandidateHard { get; init; }

    /// <summary>
    /// Aggregate soft (graded / partial-credit) score of the candidate in [0, 1].
    /// Required even when <see cref="Metric"/> is <see cref="GateMetric.Hard"/> — pass 0.0
    /// explicitly to make the choice visible at the call site (a silent default-zero on
    /// a Soft/Mixed metric would stall training by always rejecting).
    /// </summary>
    public required double CandidateSoft { get; init; }

    /// <summary>The currently-active skill at the moment of the comparison.</summary>
    public required string CurrentSkill { get; init; }

    /// <summary>Metric-space score of <see cref="CurrentSkill"/> in [0, 1].</summary>
    public required double CurrentScore { get; init; }

    /// <summary>The best-so-far skill across the training run.</summary>
    public required string BestSkill { get; init; }

    /// <summary>Metric-space score of <see cref="BestSkill"/> in [0, 1].</summary>
    public required double BestScore { get; init; }

    /// <summary>Training step at which <see cref="BestSkill"/> was last updated.</summary>
    public required int BestStep { get; init; }

    /// <summary>Current training step (recorded when a new best is promoted).</summary>
    public required int GlobalStep { get; init; }

    /// <summary>Which metric the gate compares on. Defaults to <see cref="GateMetric.Hard"/>.</summary>
    public GateMetric Metric { get; init; } = GateMetric.Hard;

    /// <summary>
    /// Soft-weight when <see cref="Metric"/> is <see cref="GateMetric.Mixed"/>. Must be in [0, 1].
    /// Ignored otherwise. Defaults to 0.5.
    /// </summary>
    public double MixedWeight { get; init; } = 0.5;
}
