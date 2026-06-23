namespace Domain.AI.SkillTraining;

/// <summary>
/// Immutable outcome of evaluating a candidate skill against the gate.
/// </summary>
/// <remarks>
/// Carries both the decision and the comparison context (current and best skills/scores)
/// so callers can persist a complete audit record without re-running anything.
/// </remarks>
public sealed record GateResult
{
    /// <summary>The accept/reject decision.</summary>
    public required GateAction Action { get; init; }

    /// <summary>
    /// The skill document active at the moment of gating (the current best or
    /// the previously accepted skill, depending on caller policy).
    /// </summary>
    public required string CurrentSkill { get; init; }

    /// <summary>The gate-metric-projected score for <see cref="CurrentSkill"/>.</summary>
    public required double CurrentScore { get; init; }

    /// <summary>The running best skill document seen so far.</summary>
    public required string BestSkill { get; init; }

    /// <summary>The gate-metric-projected score for <see cref="BestSkill"/>.</summary>
    public required double BestScore { get; init; }

    /// <summary>
    /// The training step at which the running best was last updated. Lets callers
    /// compute "epochs since last improvement" for patience-based early stopping.
    /// </summary>
    public required int BestStep { get; init; }

    /// <summary>
    /// The candidate skill document the gate just evaluated. May be the same as
    /// <see cref="BestSkill"/> when <see cref="Action"/> is <see cref="GateAction.AcceptNewBest"/>.
    /// </summary>
    public required string CandidateSkill { get; init; }

    /// <summary>The gate-metric-projected score for the candidate.</summary>
    public required double CandidateScore { get; init; }

    /// <summary>
    /// The candidate's metric-projected score on the held-in (proposer-visible) split. Populated by
    /// the two-split non-regression gate so the audit record shows the held-in evidence behind the
    /// decision. Remains <c>0.0</c> for the single-split gate, which never consults a held-in split.
    /// </summary>
    public double CandidateHeldInScore { get; init; }

    /// <summary>
    /// The current skill's metric-projected held-in score at gate time, i.e. the baseline the
    /// candidate's held-in score was compared against. Populated by the two-split gate; <c>0.0</c>
    /// for the single-split gate.
    /// </summary>
    public double CurrentHeldInScore { get; init; }
}
