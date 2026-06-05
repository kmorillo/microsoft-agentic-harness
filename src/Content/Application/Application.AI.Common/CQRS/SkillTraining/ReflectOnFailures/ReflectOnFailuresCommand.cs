using Domain.AI.SkillTraining;
using Domain.Common;
using MediatR;

namespace Application.AI.Common.CQRS.SkillTraining.ReflectOnFailures;

/// <summary>
/// Asks the configured <c>IPatchProposer</c> to reflect on a batch of rollout outcomes and
/// emit a proposed <see cref="Patch"/> against the current skill document.
/// </summary>
/// <remarks>
/// <para>
/// This is the CQRS surface for the "reflect" stage of the training loop. The orchestrator
/// in Phase 4 calls this once per training step (or once per rollout sub-batch when fan-out
/// is enabled), then feeds the resulting patches through <c>IPatchAggregator</c> and
/// <c>IEditSelector</c> before applying.
/// </para>
/// <para>
/// Failure modes: a misbehaving proposer that returns null, a parse failure, or a
/// cancellation. The handler converts these into <see cref="Result{T}"/> failures rather
/// than throwing through MediatR.
/// </para>
/// </remarks>
public sealed record ReflectOnFailuresCommand : IRequest<Result<Patch>>
{
    /// <summary>The skill document the rollouts were produced against.</summary>
    public required string CurrentSkill { get; init; }

    /// <summary>Rollout outcomes the optimizer should reflect on.</summary>
    public required IReadOnlyList<RolloutResult> Rollouts { get; init; }

    /// <summary>
    /// Cross-epoch optimizer strategy memory (see <c>SkillTrainingCheckpoint.MetaSkillMemory</c>).
    /// Empty on the first epoch.
    /// </summary>
    public string MetaSkillMemory { get; init; } = string.Empty;

    /// <summary>
    /// When true, success rollouts are passed alongside failures so the optimizer can codify
    /// winning patterns; when false only failures are surfaced. Defaults to true.
    /// </summary>
    public bool IncludeSuccesses { get; init; } = true;
}
