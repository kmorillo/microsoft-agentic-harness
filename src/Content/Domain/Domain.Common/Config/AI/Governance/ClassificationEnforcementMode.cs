namespace Domain.Common.Config.AI.Governance;

/// <summary>
/// Governs how strongly resolved data-classification decisions are applied on the agent's live
/// tool-call path. Separate from the per-asset <see cref="ClassificationAction"/>: the action is
/// <em>what</em> the policy decided, the mode is <em>how forcefully</em> that decision is enforced.
/// </summary>
/// <remarks>
/// Defaults to <see cref="Off"/> so cloning the template never requires a configured Purview tenant —
/// classification enforcement is strictly opt-in, mirroring <c>GovernanceConfig.EnforceToolInvocation</c>.
/// </remarks>
public enum ClassificationEnforcementMode
{
    /// <summary>
    /// Classification is disabled. No provider is consulted and no per-invocation cost is incurred.
    /// The default.
    /// </summary>
    Off,

    /// <summary>
    /// Classification runs and every decision is recorded (audit log + metrics), but execution is never
    /// blocked or redacted — the gate observes only. The safe first step for a new deployment: it
    /// surfaces what <em>would</em> be blocked without breaking the agent.
    /// </summary>
    Audit,

    /// <summary>
    /// Classification runs and decisions are enforced — a <see cref="ClassificationAction.Block"/>
    /// denies the tool call and a <see cref="ClassificationAction.Redact"/> strips the content.
    /// </summary>
    Enforce
}
