using Domain.AI.Changes;

namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// Per-target-type adapter that actually applies a proposal's diff. The
/// <c>MergeGate</c> resolves the right applier for the proposal's
/// <see cref="ChangeTarget.Kind"/> from keyed DI and invokes <see cref="ApplyAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only mutator in the pipeline.</strong> No other gate, no other service,
/// is permitted to alter the target. This narrows the security review surface to
/// the appliers themselves — a single small set of files an operator can audit
/// for "what can the agent actually change?".
/// </para>
/// <para>
/// Implementations are registered keyed by <see cref="ChangeTargetKind"/>:
/// <code>services.AddKeyedSingleton&lt;IChangeApplier&gt;(ChangeTargetKind.GitRepo, ...);</code>
/// One applier per target kind. Consumer-defined target kinds register their
/// own appliers under their own key.
/// </para>
/// <para>
/// Appliers must never invoke imperative APIs against production — for Kubernetes
/// targets, write the manifest into the GitOps repo and let the controller
/// reconcile; for IaC targets, drive the backend's plan/apply cycle and capture
/// the plan as evidence. Direct <c>kubectl apply</c> or ad-hoc cloud SDK mutation
/// is forbidden because it bypasses GitOps history and breaks audit reconstruction.
/// </para>
/// </remarks>
public interface IChangeApplier
{
    /// <summary>The target kind this applier handles. Used as the keyed-DI lookup key by the MergeGate.</summary>
    ChangeTargetKind TargetKind { get; }

    /// <summary>
    /// Apply the proposal's diff to its target.
    /// </summary>
    /// <param name="proposal">The proposal whose diff is being applied. <c>proposal.Target.Kind</c> must equal <see cref="TargetKind"/>.</param>
    /// <param name="context">Per-evaluation orchestrator context (mode, correlation id, etc.).</param>
    /// <param name="cancellationToken">Cancellation token honored by long-running apply operations.</param>
    /// <returns>A <see cref="ChangeApplyResult"/> indicating success (with applier-specific artifact reference) or failure (with reason).</returns>
    Task<ChangeApplyResult> ApplyAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken);
}
