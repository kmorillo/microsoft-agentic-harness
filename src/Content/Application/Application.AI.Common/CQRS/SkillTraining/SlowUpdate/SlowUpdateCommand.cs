using Domain.AI.SkillTraining;
using Domain.Common;
using MediatR;

namespace Application.AI.Common.CQRS.SkillTraining.SlowUpdate;

/// <summary>
/// Paired longitudinal comparison: given rollouts of two skill versions over the same items,
/// classify each into improved/regressed/persistent_fail/stable_success and synthesize
/// guidance for the optimizer's next epoch.
/// </summary>
/// <remarks>
/// Port of SkillOpt's slow update at epoch boundaries — designed to detect and counter
/// catastrophic forgetting. The orchestrator runs the two skills against the same item ids
/// (via <c>RolloutBatch.ItemIds</c>), then dispatches this command.
/// </remarks>
public sealed record SlowUpdateCommand : IRequest<Result<SlowUpdateAnalysis>>
{
    /// <summary>Rollouts produced by the previous epoch's skill, keyed by item id.</summary>
    public required IReadOnlyList<RolloutResult> PriorRollouts { get; init; }

    /// <summary>Rollouts produced by the current epoch's skill over the same items.</summary>
    public required IReadOnlyList<RolloutResult> CurrentRollouts { get; init; }
}
