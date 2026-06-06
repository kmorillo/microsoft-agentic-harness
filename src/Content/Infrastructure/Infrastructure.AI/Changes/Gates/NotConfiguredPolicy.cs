using Application.AI.Common.Interfaces.Changes;
using Domain.AI.Changes;

namespace Infrastructure.AI.Changes.Gates;

/// <summary>
/// Fail-loud default <see cref="IChangeProposalPolicy"/> wired into DI when no
/// real policy is registered. Throws on first use so a consumer that turns on
/// <c>ChangesConfig.Enabled</c> without wiring real policies sees the
/// misconfiguration immediately — not silently passing every proposal.
/// </summary>
/// <remarks>
/// Same fail-loud pattern as <see cref="NotConfiguredValidator"/>. Consumers
/// who genuinely want no policies registered should not register the
/// <see cref="NotConfiguredPolicy"/> at all — its sole purpose is to scream
/// when DI wiring is incomplete.
/// </remarks>
public sealed class NotConfiguredPolicy : IChangeProposalPolicy
{
    /// <inheritdoc />
    public string Key => "not_configured";

    /// <inheritdoc />
    public Task<IReadOnlyList<PolicyFinding>> EvaluateAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            "No IChangeProposalPolicy is registered. " +
            "Register at least one via services.AddSingleton<IChangeProposalPolicy, MyPolicy>() " +
            "before enabling AppConfig.AI.Changes.Enabled, or remove the 'policy' key " +
            "from the proposal's RequiredGates if your skill genuinely does not need policy checks. " +
            "PR-2 ships no real policy implementations; PR-9 (GitOps) and PR-10 (IaC) plug in " +
            "Checkov / OPA adapters as their first concrete policies.");
    }
}
