namespace Domain.AI.Changes;

/// <summary>
/// One append-only entry in a <see cref="ChangeProposal"/>'s gate history. Recorded
/// every time a gate evaluates the proposal — including <see cref="GateAction.Defer"/>
/// retries, so the full deliberation trail is reconstructable from history alone.
/// </summary>
/// <remarks>
/// <para>
/// History entries are immutable. The orchestrator appends to the history list on each
/// gate evaluation; nothing in the harness mutates or removes prior entries. Reviewers
/// reconstruct timelines by reading history in append order.
/// </para>
/// <para>
/// <see cref="ReviewerId"/> is populated only for <c>ApprovalGate</c> entries where a
/// human (or auto-approver) signed off; null for all other gates. <see cref="DurationMs"/>
/// captures wall-clock evaluation time and feeds latency dashboards.
/// </para>
/// </remarks>
public sealed record GateDecision
{
    /// <summary>UTC timestamp the gate finished evaluation (not when it started).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The keyed-DI key of the gate that produced this decision (e.g. <c>self_validation</c>, <c>policy</c>, <c>approval</c>, <c>merge</c>).</summary>
    public required string GateKey { get; init; }

    /// <summary>The decision the gate reached.</summary>
    public required GateAction Action { get; init; }

    /// <summary>The gate's human-readable reason. May be empty for <see cref="GateAction.Pass"/>; required for <see cref="GateAction.Fail"/> and <see cref="GateAction.Defer"/>.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Optional <c>sha256:</c>-prefixed hash referencing bulk evidence in the content-addressed evidence store. Null when the gate produced no bulk evidence.</summary>
    public string? EvidenceHash { get; init; }

    /// <summary>For <c>ApprovalGate</c> entries: the id of the human (or auto-approver) that signed off. Null for all other gates.</summary>
    public string? ReviewerId { get; init; }

    /// <summary>Wall-clock time the gate took to evaluate, in milliseconds. Feeds latency dashboards and helps tune defer-backoff policies.</summary>
    public required long DurationMs { get; init; }
}
