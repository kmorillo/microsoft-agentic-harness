using Domain.AI.Changes;
using Domain.AI.GitOps;
using Domain.Common;

namespace Application.AI.Common.Interfaces.GitOps;

/// <summary>
/// Translates a controller-emitted <see cref="RemediationPlan"/> into a
/// <c>ChangeProposal</c> and submits it through the PR-2 gate pipeline via
/// <c>SubmitChangeProposalCommand</c>. The single sanctioned write path for
/// the GitOps skill pack — no other code in <c>Infrastructure.AI.GitOps</c>
/// may write to a Git repo or to a cluster.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher carries the controller-of-origin breadcrumb and the drift
/// evidence hash through onto the resulting <see cref="ChangeProposal"/>'s
/// audit chain. The proposal's <see cref="ChangeProposal.BlastRadius"/> uses
/// the plan's <see cref="RemediationPlan.ProposedBlastRadius"/> as advisory;
/// the gate resolver may revise it during pipeline evaluation.
/// </para>
/// <para>
/// Repo edits inside the proposal are intended to be routed through the
/// Workspace skill via the A2A surface — the GitOps skill pack does NOT write
/// files itself. The dispatcher delegates that wire-up to the proposal
/// pipeline (the <c>MergeGate</c>'s applier), keeping responsibilities clean:
/// dispatcher submits, pipeline gates, applier writes.
/// </para>
/// </remarks>
public interface IGitOpsRemediationDispatcher
{
    /// <summary>
    /// Submit the given <paramref name="plan"/> as a <c>ChangeProposal</c>
    /// through the PR-2 pipeline.
    /// </summary>
    /// <param name="plan">The controller-emitted remediation plan to submit.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> wrapping the persisted
    /// <see cref="ChangeProposal"/> (post-id-derivation, in <c>Draft</c>)
    /// on success; <see cref="Result{T}.Fail(string[])"/> with stable
    /// <c>gitops.remediation.*</c> codes on validation or pipeline failure.
    /// Idempotent under the PR-2 deterministic id derivation — re-submitting
    /// the same plan returns the prior proposal verbatim.
    /// </returns>
    Task<Result<ChangeProposal>> DispatchAsync(RemediationPlan plan, CancellationToken cancellationToken);
}
