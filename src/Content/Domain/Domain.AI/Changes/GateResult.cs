namespace Domain.AI.Changes;

/// <summary>
/// The outcome a <c>IChangeProposalGate</c> returns when it evaluates a
/// <see cref="ChangeProposal"/>. Carries the decision, a human-readable reason, and
/// optional cross-references to bulk evidence and retry timing.
/// </summary>
/// <remarks>
/// <para>
/// Construct via the static factories (<see cref="Pass"/>, <see cref="Fail"/>,
/// <see cref="Defer"/>) so the invariants for each variant are enforced at the call
/// site — for example <see cref="Defer"/> requires a positive
/// <see cref="RetryAfter"/>, while <see cref="Pass"/> does not.
/// </para>
/// <para>
/// <see cref="EvidenceHash"/> points at content-addressed evidence (validator output,
/// policy findings) stored by the orchestrator alongside the JSONL audit line. This
/// keeps audit lines small while leaving the full evidence recoverable. The hash is
/// optional — gates that produce no bulk evidence (a cheap policy check, for example)
/// can omit it.
/// </para>
/// </remarks>
public sealed record GateResult
{
    /// <summary>The tri-state decision: <see cref="GateAction.Pass"/>, <see cref="GateAction.Fail"/>, or <see cref="GateAction.Defer"/>.</summary>
    public required GateAction Action { get; init; }

    /// <summary>
    /// A short human-readable explanation. Required for <see cref="GateAction.Fail"/>
    /// and <see cref="GateAction.Defer"/>; optional for <see cref="GateAction.Pass"/>.
    /// Recorded verbatim in the audit history.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Optional <c>sha256:</c>-prefixed hash of bulk evidence stored by the orchestrator
    /// in its content-addressed evidence store. Null when the gate has no evidence to
    /// persist beyond its reason.
    /// </summary>
    public string? EvidenceHash { get; init; }

    /// <summary>
    /// For <see cref="GateAction.Defer"/> only: the wall-clock interval the orchestrator
    /// should wait before re-evaluating this gate. Null for <see cref="GateAction.Pass"/>
    /// and <see cref="GateAction.Fail"/>.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Construct a Pass result. <paramref name="reason"/> is optional context;
    /// <paramref name="evidenceHash"/> is the optional content-addressed evidence reference.
    /// </summary>
    public static GateResult Pass(string reason = "", string? evidenceHash = null) => new()
    {
        Action = GateAction.Pass,
        Reason = reason,
        EvidenceHash = evidenceHash
    };

    /// <summary>
    /// Construct a Fail result. <paramref name="reason"/> is required and must be non-empty;
    /// <paramref name="evidenceHash"/> is the optional evidence reference.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is null or whitespace.</exception>
    public static GateResult Fail(string reason, string? evidenceHash = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Fail requires a non-empty reason for the audit trail.", nameof(reason));
        }

        return new GateResult
        {
            Action = GateAction.Fail,
            Reason = reason,
            EvidenceHash = evidenceHash
        };
    }

    /// <summary>
    /// Construct a Defer result. <paramref name="retryAfter"/> must be strictly positive;
    /// <paramref name="reason"/> is required and must be non-empty.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is null or whitespace, or <paramref name="retryAfter"/> is non-positive.</exception>
    public static GateResult Defer(string reason, TimeSpan retryAfter, string? evidenceHash = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Defer requires a non-empty reason explaining what the orchestrator is waiting on.", nameof(reason));
        }

        if (retryAfter <= TimeSpan.Zero)
        {
            throw new ArgumentException("Defer requires a strictly positive retry interval.", nameof(retryAfter));
        }

        return new GateResult
        {
            Action = GateAction.Defer,
            Reason = reason,
            EvidenceHash = evidenceHash,
            RetryAfter = retryAfter
        };
    }
}
