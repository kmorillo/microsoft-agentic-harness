namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// Per-evaluation context passed to every <c>IChangeProposalGate</c> alongside the
/// proposal itself. Carries orchestrator state that gates need but proposals do not
/// hold — execution mode, attempt counter, the evaluation clock.
/// </summary>
/// <remarks>
/// <para>
/// Immutable. The orchestrator constructs a new <see cref="GateContext"/> per gate
/// evaluation; gates never mutate it. Anything a gate wants to communicate back to
/// the orchestrator (or to a future gate evaluation) must travel on the returned
/// <c>GateResult</c> or be persisted on the proposal's <c>History</c>.
/// </para>
/// <para>
/// <see cref="AttemptCount"/> starts at 1 for the first evaluation of this gate
/// against this proposal and increments on every <c>Defer</c> retry. Gates that
/// implement back-off or "after N defers, give up and fail" policies read it; gates
/// that don't can ignore it.
/// </para>
/// </remarks>
public sealed record GateContext
{
    /// <summary>The current orchestrator mode — Shadow suppresses the merge effect but exercises every other gate normally.</summary>
    public required OrchestratorMode Mode { get; init; }

    /// <summary>
    /// The 1-based attempt count for this specific gate against this proposal.
    /// Increments on every <c>Defer</c> retry; resets to 1 if the orchestrator
    /// requeues the proposal at a different gate.
    /// </summary>
    public required int AttemptCount { get; init; }

    /// <summary>The UTC wall-clock instant the orchestrator started this gate evaluation. Gates use it to compute their own <c>GateDecision.DurationMs</c>.</summary>
    public required DateTimeOffset EvaluatedAt { get; init; }

    /// <summary>
    /// A correlation id unique to this orchestrator run. Gates emit it on every log
    /// line and OpenTelemetry span so an operator can stitch the full pipeline
    /// timeline together for one proposal across services.
    /// </summary>
    public required string CorrelationId { get; init; }
}
