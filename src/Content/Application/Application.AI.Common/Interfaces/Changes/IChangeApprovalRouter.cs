using Domain.AI.Changes;

namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// Routes a <see cref="ChangeProposal"/> that has reached the approval gate to
/// whatever approval surface the host has wired up — typically the existing
/// <c>IEscalationService</c>, but a consumer may swap for Slack, Teams, GitHub
/// PR comments, etc.
/// </summary>
/// <remarks>
/// <para>
/// Decoupling the gate from the routing impl lets the gate stay simple ("queue
/// for human, return Defer") while the routing impl carries the integration
/// detail. The default implementation
/// (<c>EscalationServiceApprovalRouter</c>) creates an <c>EscalationRequest</c>
/// derived from the proposal and dispatches via <c>IEscalationService</c>.
/// </para>
/// <para>
/// Idempotency: <see cref="RouteAsync"/> is called from inside the
/// <c>ApprovalGate</c>, which the orchestrator invokes once per attempt.
/// Implementations that have a separate idempotency contract (the
/// EscalationService one is keyed by escalation id) should derive their
/// idempotency key from <see cref="ChangeProposal.Id"/> + a per-attempt salt
/// (e.g. attempt count) so a Defer retry doesn't enqueue duplicates.
/// </para>
/// </remarks>
public interface IChangeApprovalRouter
{
    /// <summary>
    /// Surface the proposal to the configured approval audience.
    /// </summary>
    /// <param name="proposal">The proposal needing approval. Must not be mutated.</param>
    /// <param name="context">Per-evaluation orchestrator context — <c>AttemptCount</c> drives idempotency salt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RouteAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken);
}
