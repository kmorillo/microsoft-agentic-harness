using Domain.AI.Changes;

namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// One bounded validator used by the <c>SelfValidationGate</c>. Per-target-type
/// validators register under a keyed-DI key matching the proposal's
/// <see cref="ChangeTargetKind"/>; the gate enumerates them via
/// <c>IServiceProvider.GetKeyedServices</c> and aggregates their results.
/// </summary>
/// <remarks>
/// <para>
/// Examples shipped by future PRs (under the <c>ChangeTargetKind</c> keys):
/// </para>
/// <list type="bullet">
///   <item><description><c>GitRepo</c>: <c>run_tests</c>, <c>run_lint</c> (PR-8 Workspace skill).</description></item>
///   <item><description><c>KubernetesResource</c>: <c>kubectl_dry_run</c> (PR-9 GitOps skill).</description></item>
///   <item><description><c>IacDeployment</c>: <c>terraform_plan</c> / <c>bicep_what_if</c> (PR-10 IaC skill).</description></item>
/// </list>
/// <para>
/// Consumers add additional validators under the same target-kind keys without
/// forking the gate. The gate fails the proposal as soon as any single
/// validator returns <see cref="GateAction.Fail"/> — short-circuit semantics
/// keep validator runtime bounded.
/// </para>
/// </remarks>
public interface IChangeProposalValidator
{
    /// <summary>
    /// Short stable identifier for this validator (e.g. <c>run_tests</c>).
    /// Surfaces in the resulting <c>GateDecision.Reason</c> and in dashboards
    /// so an operator can tell which specific validator blocked a proposal
    /// when the SelfValidationGate fails.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Run the validator against the proposal.
    /// </summary>
    /// <param name="proposal">The proposal under evaluation. Must not be mutated.</param>
    /// <param name="context">Per-evaluation orchestrator context — gates pass this through to the validator.</param>
    /// <param name="cancellationToken">Cancellation token honored by long-running validators.</param>
    /// <returns>A <see cref="GateResult"/> with <see cref="GateAction.Pass"/> / <see cref="GateAction.Fail"/> / <see cref="GateAction.Defer"/>.</returns>
    Task<GateResult> ValidateAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken);
}
