namespace Domain.AI.Changes;

/// <summary>
/// The lifecycle state of a <see cref="ChangeProposal"/>. Transitions are governed by
/// <see cref="ChangeProposalStateTransitions"/>; illegal transitions throw rather than
/// silently no-op so state-machine bugs surface at the call site.
/// </summary>
/// <remarks>
/// <para>
/// Normal happy path: <see cref="Draft"/> → <see cref="Validating"/> →
/// <see cref="AwaitingApproval"/> → <see cref="Approved"/> → <see cref="Merging"/> →
/// <see cref="Merged"/>. Each step corresponds to a gate completing.
/// </para>
/// <para>
/// Terminal states are <see cref="Merged"/>, <see cref="Rejected"/>, and
/// <see cref="Cancelled"/>. A proposal in a terminal state can never transition again;
/// re-submitting the same idempotent proposal id returns the terminal result.
/// </para>
/// </remarks>
public enum ChangeProposalStatus
{
    /// <summary>
    /// The proposal has been created but no gate has yet evaluated it. The orchestrator
    /// moves it forward as soon as it is picked up.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// The <c>SelfValidationGate</c> and/or <c>PolicyGate</c> are evaluating the proposal.
    /// On Pass, advances to <see cref="AwaitingApproval"/>. On Fail, advances to
    /// <see cref="Rejected"/>. On Defer, stays here with a retry scheduled.
    /// </summary>
    Validating = 1,

    /// <summary>
    /// Validation gates passed; the <c>ApprovalGate</c> is awaiting a decision from
    /// the configured approver workflow (human or auto-approve under the autonomy tier).
    /// </summary>
    AwaitingApproval = 2,

    /// <summary>
    /// Approval recorded; the proposal is queued for the <c>MergeGate</c>. This is a
    /// transient state — the orchestrator moves to <see cref="Merging"/> immediately
    /// on pickup.
    /// </summary>
    Approved = 3,

    /// <summary>
    /// The <c>MergeGate</c> is currently applying the diff to the target via the
    /// target's <c>IChangeApplier</c>. On success, advances to <see cref="Merged"/>;
    /// on failure, advances to <see cref="Rejected"/> with the apply error captured.
    /// </summary>
    Merging = 4,

    /// <summary>
    /// Terminal — the diff has been applied to the target. The <c>ChangeProposalMerged</c>
    /// domain event is emitted on entry to this state.
    /// </summary>
    Merged = 5,

    /// <summary>
    /// Terminal — a gate returned Fail, an approver rejected the proposal, or the
    /// merge operation failed. The reason is recorded in the proposal's gate history.
    /// </summary>
    Rejected = 6,

    /// <summary>
    /// Terminal — the submitter (agent or human) cancelled the proposal before it
    /// reached <see cref="Merged"/> or <see cref="Rejected"/>. Distinct from rejection
    /// because no gate produced an adverse decision.
    /// </summary>
    Cancelled = 7
}
