using Domain.AI.SkillTraining;

namespace Application.AI.Common.Interfaces.SkillTraining;

/// <summary>
/// The "optimizer" role of the skill-training loop: reflects on rollout trajectories and
/// proposes a <see cref="Patch"/> against the current skill document.
/// </summary>
/// <remarks>
/// <para>
/// Conceptually equivalent to the backward pass in deep learning — turns scored
/// trajectories into a gradient (here, a structured patch). Implementations are usually
/// agent-backed (a keyed <c>"skill-optimizer"</c> LLM agent that consumes the trajectory
/// and emits a JSON patch), but the interface accepts any pure substitute, which makes
/// the rest of the training loop trivially unit-testable.
/// </para>
/// <para>
/// Implementations must be re-entrant; the training loop may invoke the proposer many
/// times per step (e.g. one call per rollout sub-batch) and aggregate the resulting
/// patches in <see cref="IPatchAggregator"/>.
/// </para>
/// </remarks>
public interface IPatchProposer
{
    /// <summary>
    /// Reflects on the rollout outcomes in <paramref name="input"/> and produces a single
    /// proposed patch to the skill document.
    /// </summary>
    /// <param name="input">Current skill, rollouts to reflect on, and any meta-skill memory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A patch — possibly empty (<see cref="Patch.Edits"/> count zero) if the proposer
    /// decided no change is warranted.</returns>
    Task<Patch> ProposeAsync(ReflectionInput input, CancellationToken cancellationToken);
}
