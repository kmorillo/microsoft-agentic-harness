namespace Domain.AI.SkillTraining;

/// <summary>
/// Outcome of running a single rollout (one item from the train/val split) with a candidate skill.
/// </summary>
/// <remarks>
/// <para>
/// Port of SkillOpt's <c>RolloutResult</c>: every rollout must produce an item identifier and
/// two scalar scores — <see cref="Hard"/> (exact-match / binary) and <see cref="Soft"/>
/// (partial-credit / graded). Both in [0, 1]. The trainer aggregates these into batch-level
/// hard/soft scores for the gate; the optimizer reads the <see cref="Trajectory"/> when
/// proposing edits.
/// </para>
/// <para>
/// <see cref="ItemId"/> is the stable id of the underlying eval case so the optimizer can
/// cite individual failures by reference, and so paired (longitudinal) rollouts can match
/// items across epochs.
/// </para>
/// </remarks>
public sealed record RolloutResult
{
    /// <summary>Stable identifier of the underlying eval case / rollout item.</summary>
    public required string ItemId { get; init; }

    /// <summary>Exact-match / binary score in [0, 1].</summary>
    public required double Hard { get; init; }

    /// <summary>Graded / partial-credit score in [0, 1].</summary>
    public required double Soft { get; init; }

    /// <summary>
    /// Optional trajectory text — the agent's reasoning, intermediate outputs, tool calls,
    /// and final answer for this item. The optimizer reflects on this when proposing edits.
    /// Empty when the rollout produced no diagnostic detail.
    /// </summary>
    public string Trajectory { get; init; } = string.Empty;

    /// <summary>
    /// Optional task-type tag for stratified sampling (e.g. <c>"arithmetic"</c>, <c>"rag"</c>).
    /// Sourced from the eval case tags. Empty when unset.
    /// </summary>
    public string TaskType { get; init; } = string.Empty;

    /// <summary>
    /// Tolerance for the <see cref="IsSuccess"/> threshold. A hard score within this band of 1.0
    /// is treated as success — protects against floating-point accumulation drift in graded-binary
    /// scorers (e.g. 0.99999998 from a chain of partial-credit sums).
    /// </summary>
    public const double SuccessEpsilon = 1e-6;

    /// <summary>
    /// True when the rollout's binary score indicates success — i.e.
    /// <see cref="Hard"/> ≥ 1.0 − <see cref="SuccessEpsilon"/>.
    /// </summary>
    public bool IsSuccess => Hard >= 1.0 - SuccessEpsilon;
}
