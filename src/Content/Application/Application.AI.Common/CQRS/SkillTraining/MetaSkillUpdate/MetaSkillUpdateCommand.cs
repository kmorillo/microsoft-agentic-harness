using Domain.Common;
using MediatR;

namespace Application.AI.Common.CQRS.SkillTraining.MetaSkillUpdate;

/// <summary>
/// Updates the cross-epoch meta-skill memory for a training run — strategy notes the optimizer
/// accumulates and re-injects as context on subsequent reflection steps.
/// </summary>
/// <remarks>
/// Port of SkillOpt's <c>optimizer/meta_skill.py</c>. Stored via <c>IKnowledgeMemory</c> so the
/// memory persists across sessions and is recallable by future training runs of the same skill.
/// </remarks>
public sealed record MetaSkillUpdateCommand : IRequest<Result<string>>
{
    /// <summary>Identifier of the training run.</summary>
    public required string RunId { get; init; }

    /// <summary>Skill identifier being trained.</summary>
    public required string SkillId { get; init; }

    /// <summary>Epoch that just completed (1-based).</summary>
    public required int Epoch { get; init; }

    /// <summary>Skill content active at end-of-epoch.</summary>
    public required string CurrentSkill { get; init; }

    /// <summary>Current best score at end-of-epoch.</summary>
    public required double CurrentScore { get; init; }

    /// <summary>Prior epoch's meta memory, if any. Empty on the first epoch.</summary>
    public string PriorMemory { get; init; } = string.Empty;
}
