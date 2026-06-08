using Domain.AI.GitOps;
using Domain.Common;

namespace Application.AI.Common.Interfaces.GitOps;

/// <summary>
/// Controller-neutral GitOps surface. Single interface implemented once per
/// supported controller (Flux, Argo CD) so the three GitOps skills can be
/// authored once against a stable abstraction.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No mutation methods.</strong> The interface deliberately exposes no
/// <c>Apply</c>, <c>Delete</c>, or <c>Upgrade</c> verbs. Every state-changing
/// remediation surfaces as a <c>ChangeProposal</c> against a
/// <c>GitRepoTarget</c> via <see cref="ProposeRemediationAsync"/>; the
/// <c>IGitOpsRemediationDispatcher</c> wraps the resulting
/// <see cref="RemediationPlan"/> in a <c>SubmitChangeProposalCommand</c>.
/// The interface forbidding direct mutation is the load-bearing safety
/// invariant of the PR-9 skill pack.
/// </para>
/// <para>
/// <strong>No controller-specific types.</strong> Inputs and outputs are
/// drawn exclusively from <c>Domain.AI.GitOps.*</c>. Implementations live in
/// <c>Infrastructure.AI.GitOps.{Flux,ArgoCd}</c> and never leak their native
/// types into the Application layer — enforced by a reflection-based
/// architectural test in PR-9 layer 5.
/// </para>
/// <para>
/// Implementations are registered with keyed DI by the lowercase string form
/// of <see cref="GitOpsControllerKind"/> (<c>"flux"</c> / <c>"argocd"</c>).
/// Configuration selects the active controller; skills resolve through that
/// key, never by concrete type.
/// </para>
/// </remarks>
public interface IGitOpsController
{
    /// <summary>The controller-of-origin discriminator for this implementation.</summary>
    GitOpsControllerKind Kind { get; }

    /// <summary>
    /// Detect drift between desired state (declared in Git) and actual state
    /// (running in the cluster). Returns a controller-neutral
    /// <see cref="DriftReport"/> regardless of which controller is active.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> wrapping the drift report on success;
    /// <see cref="Result{T}.Fail(string[])"/> with stable <c>gitops.*</c> codes on
    /// controller-side failure (unreachable API, auth failure, malformed response).
    /// Cancellation surfaces as <c>OperationCanceledException</c>.
    /// </returns>
    Task<Result<DriftReport>> DetectDriftAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Read the controller's view of cluster-level health. Returns a
    /// controller-neutral <see cref="ClusterHealth"/> snapshot regardless of
    /// which controller is active.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> wrapping the health snapshot on success;
    /// <see cref="Result{T}.Fail(string[])"/> with stable <c>gitops.*</c> codes on
    /// controller-side failure.
    /// </returns>
    Task<Result<ClusterHealth>> GetClusterHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Translate a <see cref="DriftReport"/> into a controller-neutral
    /// <see cref="RemediationPlan"/> — the ordered set of Git edits that would
    /// re-converge the cluster. Read-only: this method NEVER applies any change
    /// to the cluster or to the Git repo; the caller submits the resulting
    /// plan via <c>IGitOpsRemediationDispatcher</c>, which wraps it in a
    /// <c>SubmitChangeProposalCommand</c>.
    /// </summary>
    /// <param name="drift">The drift report to remediate. Must come from this controller.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> wrapping the remediation plan on success.
    /// <see cref="Result{T}.Fail(string[])"/> when the drift cannot be remediated
    /// declaratively (e.g. live-only resources without a Git source) with a
    /// stable <c>gitops.*</c> code explaining the obstruction.
    /// </returns>
    Task<Result<RemediationPlan>> ProposeRemediationAsync(DriftReport drift, CancellationToken cancellationToken);
}
