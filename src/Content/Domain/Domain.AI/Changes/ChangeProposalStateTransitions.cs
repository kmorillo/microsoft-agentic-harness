using System.Collections.Frozen;

namespace Domain.AI.Changes;

/// <summary>
/// The canonical state-machine for <see cref="ChangeProposalStatus"/>. Encodes which
/// transitions are legal and provides predicates the aggregate uses to reject illegal
/// transitions at the call site.
/// </summary>
/// <remarks>
/// <para>
/// Held as a flat lookup table (frozen at type-init) rather than a switch statement so
/// the legal set is inspectable from tests and from diagnostics tooling without
/// re-encoding the rules in two places.
/// </para>
/// <para>
/// Terminal states (<see cref="ChangeProposalStatus.Merged"/>,
/// <see cref="ChangeProposalStatus.Rejected"/>, <see cref="ChangeProposalStatus.Cancelled"/>)
/// have no outgoing transitions and never appear as a <c>from</c> key.
/// </para>
/// </remarks>
public static class ChangeProposalStateTransitions
{
    private static readonly FrozenDictionary<ChangeProposalStatus, FrozenSet<ChangeProposalStatus>> _legalTransitions
        = BuildLegalTransitions();

    /// <summary>
    /// The set of states that have no outgoing transitions. Once a proposal enters a
    /// terminal state it cannot move again; re-submission of the same idempotent id
    /// returns the cached terminal result.
    /// </summary>
    public static readonly FrozenSet<ChangeProposalStatus> TerminalStates = new[]
    {
        ChangeProposalStatus.Merged,
        ChangeProposalStatus.Rejected,
        ChangeProposalStatus.Cancelled
    }.ToFrozenSet();

    /// <summary>
    /// Returns the set of states that <paramref name="from"/> may legally transition to.
    /// Empty when <paramref name="from"/> is a terminal state.
    /// </summary>
    /// <param name="from">The current status.</param>
    /// <returns>The set of legal next states; empty if <paramref name="from"/> is terminal.</returns>
    public static FrozenSet<ChangeProposalStatus> LegalNext(ChangeProposalStatus from) =>
        _legalTransitions.TryGetValue(from, out var next) ? next : FrozenSet<ChangeProposalStatus>.Empty;

    /// <summary>
    /// True when transitioning from <paramref name="from"/> to <paramref name="to"/>
    /// is permitted by the state machine.
    /// </summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The proposed next status.</param>
    /// <returns>True if the transition is legal, false otherwise.</returns>
    public static bool IsLegal(ChangeProposalStatus from, ChangeProposalStatus to) =>
        LegalNext(from).Contains(to);

    /// <summary>
    /// True when <paramref name="status"/> is a terminal state (no outgoing transitions).
    /// </summary>
    /// <param name="status">The status to test.</param>
    /// <returns>True if <paramref name="status"/> is one of <see cref="TerminalStates"/>.</returns>
    public static bool IsTerminal(ChangeProposalStatus status) => TerminalStates.Contains(status);

    private static FrozenDictionary<ChangeProposalStatus, FrozenSet<ChangeProposalStatus>> BuildLegalTransitions()
    {
        var map = new Dictionary<ChangeProposalStatus, FrozenSet<ChangeProposalStatus>>
        {
            // From Draft: orchestrator picks up → Validating. Submitter can cancel.
            [ChangeProposalStatus.Draft] = new[]
            {
                ChangeProposalStatus.Validating,
                ChangeProposalStatus.Cancelled
            }.ToFrozenSet(),

            // From Validating: Pass → AwaitingApproval; Fail → Rejected; cancel allowed.
            // Defer keeps the proposal in Validating (self-loop), so Validating is also
            // in this set so the state machine treats "stay-here" as legal.
            [ChangeProposalStatus.Validating] = new[]
            {
                ChangeProposalStatus.Validating,
                ChangeProposalStatus.AwaitingApproval,
                ChangeProposalStatus.Rejected,
                ChangeProposalStatus.Cancelled
            }.ToFrozenSet(),

            // From AwaitingApproval: approver Pass → Approved; Fail/Reject → Rejected;
            // cancel allowed; Defer self-loop on AwaitingApproval is legal.
            [ChangeProposalStatus.AwaitingApproval] = new[]
            {
                ChangeProposalStatus.AwaitingApproval,
                ChangeProposalStatus.Approved,
                ChangeProposalStatus.Rejected,
                ChangeProposalStatus.Cancelled
            }.ToFrozenSet(),

            // From Approved: orchestrator picks up → Merging. Cancel still allowed
            // until the merge actually starts.
            [ChangeProposalStatus.Approved] = new[]
            {
                ChangeProposalStatus.Merging,
                ChangeProposalStatus.Cancelled
            }.ToFrozenSet(),

            // From Merging: success → Merged; failure → Rejected. No cancel — once
            // merging starts the diff is being applied and cannot be cleanly aborted.
            [ChangeProposalStatus.Merging] = new[]
            {
                ChangeProposalStatus.Merged,
                ChangeProposalStatus.Rejected
            }.ToFrozenSet(),

            // Terminal states omitted: Merged, Rejected, Cancelled have no outgoing edges.
        };

        return map.ToFrozenDictionary();
    }
}
