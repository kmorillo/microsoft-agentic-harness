namespace Domain.AI.Changes;

/// <summary>
/// The outcome a <c>IChangeProposalGate</c> reports for a <see cref="ChangeProposal"/>.
/// </summary>
/// <remarks>
/// <para>
/// Gates do not return a generic boolean — they return a tri-state so the orchestrator
/// can distinguish a definite rejection (<see cref="Fail"/>) from a temporary inability
/// to decide (<see cref="Defer"/>), the latter being legitimate when an upstream system
/// (escalation reviewer, policy provider, sandbox) needs more time.
/// </para>
/// <para>
/// Gates never return a <c>Skip</c>. A gate is either required (registered for the
/// proposal's target type and blast radius) or not registered at all; there is no
/// soft-bypass.
/// </para>
/// </remarks>
public enum GateAction
{
    /// <summary>
    /// The gate evaluated the proposal and accepts it. The orchestrator advances the
    /// proposal to the next gate in the pipeline, or marks it ready for the next stage
    /// when this is the final gate of a stage.
    /// </summary>
    Pass,

    /// <summary>
    /// The gate evaluated the proposal and rejects it. The orchestrator moves the
    /// proposal to <see cref="ChangeProposalStatus.Rejected"/> with the gate's reason
    /// captured in the audit history. No further gates run.
    /// </summary>
    Fail,

    /// <summary>
    /// The gate cannot reach a decision yet and asks the orchestrator to retry after
    /// <c>GateResult.RetryAfter</c>. The proposal stays in its current status and is
    /// requeued for evaluation. Defer is finite — orchestrator policy caps the number
    /// of consecutive defers before promoting to <see cref="Fail"/>.
    /// </summary>
    Defer
}
