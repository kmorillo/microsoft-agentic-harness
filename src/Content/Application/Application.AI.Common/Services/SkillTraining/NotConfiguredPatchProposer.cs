using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// Fail-fast placeholder <see cref="IPatchProposer"/>. Throws with explicit guidance when
/// invoked — the skill-training subsystem ships its pure components but the agent-backed
/// proposer is wiring the template consumer owns.
/// </summary>
/// <remarks>
/// Registered by default so other parts of the harness can DI-resolve <see cref="IPatchProposer"/>
/// (e.g. in tests against the orchestrator handler that provide their own stub) without forcing
/// every consumer to write a real impl on day one. Replace via a keyed or replacement
/// registration before invoking <c>TrainSkillCommand</c>.
/// </remarks>
public sealed class NotConfiguredPatchProposer : IPatchProposer
{
    /// <inheritdoc />
    public Task<Patch> ProposeAsync(ReflectionInput input, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No IPatchProposer is configured. Replace the default NotConfiguredPatchProposer " +
            "with an agent-backed implementation (e.g. AgentPatchProposer in Infrastructure.AI) " +
            "before invoking TrainSkillCommand.");
}
