using Domain.AI.Identity;

namespace Domain.AI.Changes;

/// <summary>
/// The aggregate at the center of PR-2: a proposed change against a target, with
/// a structured diff, a state-machine-governed lifecycle, an append-only audit
/// history, and a deterministic id that makes re-submission idempotent.
/// </summary>
/// <remarks>
/// <para>
/// Constructed via <see cref="Create(ChangeTarget, IReadOnlyList{ChangeEdit}, AgentIdentity, string, BlastRadius, IReadOnlyList{string}, DateTimeOffset)"/>
/// so the id derivation runs at the call site and the initial status is correct.
/// Direct construction with <c>new ChangeProposal { ... }</c> is permitted for tests
/// and persistence rehydration but bypasses id derivation.
/// </para>
/// <para>
/// Mutation is impossible — the type is a record and every state change returns a
/// new instance via <see cref="TransitionTo"/>. Two instances with the same
/// <see cref="Id"/> represent the same aggregate at different points in its
/// lifecycle. Equality and hash code therefore use <see cref="Id"/> alone rather
/// than the default structural equality (which would do a deep comparison of
/// <see cref="Diff"/>, <see cref="History"/>, and so on — wrong for an aggregate
/// and broken for record-of-collection types anyway).
/// </para>
/// </remarks>
public sealed record ChangeProposal
{
    /// <summary>The deterministic, idempotent id derived from <c>(target, diff, submittedBy, submittedAt-bucket)</c>.</summary>
    public required string Id { get; init; }

    /// <summary>A short human-readable summary of the change. Surfaces in approval prompts and audit lines.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>The target the change will apply to.</summary>
    public required ChangeTarget Target { get; init; }

    /// <summary>The ordered list of bounded edits the diff comprises.</summary>
    public required IReadOnlyList<ChangeEdit> Diff { get; init; }

    /// <summary>The submitter's estimate of the change's impact radius.</summary>
    public required BlastRadius BlastRadius { get; init; }

    /// <summary>
    /// The ordered list of gate keys the orchestrator must run, top-to-bottom. Typically
    /// derived from <see cref="Target"/>'s kind and <see cref="BlastRadius"/> by an
    /// <c>IChangeProposalGateResolver</c>; recorded on the proposal so the pipeline a
    /// historical proposal ran through is reconstructable.
    /// </summary>
    public required IReadOnlyList<string> RequiredGates { get; init; }

    /// <summary>The current lifecycle state. Transitions are governed by <see cref="ChangeProposalStateTransitions"/>.</summary>
    public required ChangeProposalStatus Status { get; init; }

    /// <summary>The agent identity that submitted the proposal — captured at submission, never re-resolved.</summary>
    public required AgentIdentity SubmittedBy { get; init; }

    /// <summary>The wall-clock submission time. Used in id derivation and surfaces in audit lines.</summary>
    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>The append-only audit history — one entry per gate evaluation (including defer retries).</summary>
    public IReadOnlyList<GateDecision> History { get; init; } = [];

    /// <summary>
    /// Construct a new proposal in <see cref="ChangeProposalStatus.Draft"/> with a
    /// deterministic <see cref="Id"/> derived from <paramref name="target"/>,
    /// <paramref name="diff"/>, <paramref name="submittedBy"/>, and the time-bucket
    /// of <paramref name="submittedAt"/>.
    /// </summary>
    /// <param name="target">The target the change will apply to.</param>
    /// <param name="diff">The ordered list of edits to apply.</param>
    /// <param name="submittedBy">The agent identity submitting the proposal.</param>
    /// <param name="summary">A short human-readable summary.</param>
    /// <param name="blastRadius">The submitter's impact-radius estimate.</param>
    /// <param name="requiredGates">The ordered gate keys to run.</param>
    /// <param name="submittedAt">The wall-clock submission time.</param>
    /// <returns>A new <see cref="ChangeProposal"/> in <see cref="ChangeProposalStatus.Draft"/>.</returns>
    public static ChangeProposal Create(
        ChangeTarget target,
        IReadOnlyList<ChangeEdit> diff,
        AgentIdentity submittedBy,
        string summary,
        BlastRadius blastRadius,
        IReadOnlyList<string> requiredGates,
        DateTimeOffset submittedAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(submittedBy);
        ArgumentNullException.ThrowIfNull(requiredGates);

        var id = ChangeProposalIdDeriver.Derive(target, diff, submittedBy, submittedAt);

        return new ChangeProposal
        {
            Id = id,
            Summary = summary ?? string.Empty,
            Target = target,
            Diff = diff,
            BlastRadius = blastRadius,
            RequiredGates = requiredGates,
            Status = ChangeProposalStatus.Draft,
            SubmittedBy = submittedBy,
            SubmittedAt = submittedAt,
            History = []
        };
    }

    /// <summary>
    /// Apply a state-machine transition. Returns a new <see cref="ChangeProposal"/>
    /// with the new status and the gate decision appended to <see cref="History"/>.
    /// </summary>
    /// <param name="next">The proposed next status. Must satisfy <see cref="ChangeProposalStateTransitions.IsLegal"/>.</param>
    /// <param name="decision">The gate decision that triggered the transition. Appended to history.</param>
    /// <returns>A new proposal instance with the updated status and appended history.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the transition is illegal under <see cref="ChangeProposalStateTransitions"/>.</exception>
    public ChangeProposal TransitionTo(ChangeProposalStatus next, GateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!ChangeProposalStateTransitions.IsLegal(Status, next))
        {
            throw new InvalidOperationException(
                $"Illegal ChangeProposal transition: {Status} → {next} (proposal {Id}).");
        }

        var history = new List<GateDecision>(History.Count + 1);
        history.AddRange(History);
        history.Add(decision);

        return this with
        {
            Status = next,
            History = history
        };
    }

    /// <summary>True when <see cref="Status"/> has no outgoing transitions.</summary>
    public bool IsTerminal => ChangeProposalStateTransitions.IsTerminal(Status);

    /// <inheritdoc/>
    /// <remarks>
    /// Aggregates equal by id — two snapshots of the same proposal at different points
    /// in its lifecycle are the same aggregate. Default record equality would do a
    /// deep structural comparison including <see cref="Diff"/> and <see cref="History"/>,
    /// which (a) is wrong for an aggregate and (b) is broken for collection-valued
    /// properties on records anyway.
    /// </remarks>
    public bool Equals(ChangeProposal? other) =>
        other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}
