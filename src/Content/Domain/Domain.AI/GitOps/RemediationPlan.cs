using Domain.AI.Changes;

namespace Domain.AI.GitOps;

/// <summary>
/// A controller-neutral remediation proposal — the result of a controller
/// taking a <see cref="DriftReport"/> and producing the ordered set of Git
/// edits that would re-converge the cluster to its declared desired state.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="RemediationPlan"/> NEVER mutates the cluster — it surfaces as
/// a <c>ChangeProposal</c> against a <see cref="GitRepoTarget"/>, gated by the
/// PR-2 pipeline. The plan carries a <see cref="ChangeTarget"/> and the
/// ordered <see cref="Edits"/>; the <c>IGitOpsRemediationDispatcher</c> wraps
/// the plan in a <c>SubmitChangeProposalCommand</c> at submission time.
/// </para>
/// <para>
/// <see cref="ProposedBlastRadius"/> is the controller's first-cut radius based
/// on drift severity and the number of resources touched. The
/// <c>IChangeProposalGateResolver</c> may revise it during gate evaluation —
/// the audit trail records both values. The default mapping is documented on
/// each <see cref="DriftSeverity"/> member.
/// </para>
/// </remarks>
public sealed record RemediationPlan
{
    /// <summary>The controller that produced this plan.</summary>
    public required GitOpsControllerKind ControllerKind { get; init; }

    /// <summary>The drift report this plan addresses. Carried for audit linkage.</summary>
    public required DriftReport SourceDrift { get; init; }

    /// <summary>
    /// The target the plan's edits will apply to. A <c>GitRepoTarget</c> in the
    /// shipping implementations; modeled as the base <see cref="ChangeTarget"/>
    /// so future targets (e.g. Helm chart registry) can be added without
    /// breaking consumers.
    /// </summary>
    public required ChangeTarget Target { get; init; }

    /// <summary>The ordered list of bounded edits the plan would apply to <see cref="Target"/>.</summary>
    public IReadOnlyList<ChangeEdit> Edits { get; init; } = [];

    /// <summary>
    /// The controller's first-cut blast radius for this plan. Gate resolution
    /// may revise it — audit records both. Critical drift always requires
    /// human approval regardless of this advisory value.
    /// </summary>
    public BlastRadius ProposedBlastRadius { get; init; } = BlastRadius.Medium;

    /// <summary>
    /// Human-readable summary that surfaces in the resulting
    /// <c>ChangeProposal</c>'s summary line and approval prompt.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}
