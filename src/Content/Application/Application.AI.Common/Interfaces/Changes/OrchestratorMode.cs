namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// The execution mode of the <c>ChangeProposalOrchestrator</c>. Drives whether the
/// final <c>MergeGate</c> actually applies the diff or stops short with the decision
/// audit-logged only.
/// </summary>
/// <remarks>
/// <para>
/// Shadow mode exists so a consumer can run the full pipeline against real proposals
/// for days or weeks before flipping a specific skill to <see cref="Live"/>. Every
/// gate evaluates and every decision is audited; only the merge effect is suppressed.
/// This lets operators tune blast-radius estimation, gate severity thresholds, and
/// approval routing against real workloads without risking unintended changes.
/// </para>
/// <para>
/// The mode is resolved per-proposal at submission time. A single agent could submit
/// proposals against Shadow targets and Live targets in the same session, so the
/// mode must travel with the proposal in <see cref="GateContext"/> rather than
/// being a global orchestrator setting.
/// </para>
/// </remarks>
public enum OrchestratorMode
{
    /// <summary>
    /// Gates evaluate, decisions are audit-logged, and the <c>MergeGate</c> short-circuits
    /// before invoking <c>IChangeApplier</c>. The proposal still advances through the
    /// state machine to <c>Merged</c> so consumers can see the full lifecycle trail,
    /// but no real-world effect occurs. The audit line records <c>"shadow"</c> so a
    /// reviewer can distinguish shadow from live trails.
    /// </summary>
    Shadow = 0,

    /// <summary>
    /// Gates evaluate, decisions are audit-logged, and the <c>MergeGate</c> invokes
    /// the target's <c>IChangeApplier</c> to actually apply the diff. This is the
    /// real production mode — the orchestrator only flips to Live per skill, per
    /// target type, on explicit operator opt-in.
    /// </summary>
    Live = 1
}
