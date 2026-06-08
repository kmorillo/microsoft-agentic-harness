namespace Domain.Common.Config.AI.IncidentResponse;

/// <summary>
/// A named bundle of incident-response policy. When the host reports an active
/// incident whose <see cref="IncidentType"/> matches this plan, the harness:
/// <list type="number">
///   <item><description>Activates the declared <see cref="SkillSet"/> on the agent.</description></item>
///   <item><description>Forces the agent's autonomy tier to <see cref="AutonomyTierOverride"/> for the duration of the incident (when non-null).</description></item>
///   <item><description>Overlays <see cref="AdditionalRequiredGates"/> on any <c>ChangeProposal</c> submitted while the incident is active — without mutating the proposal's stored <c>RequiredGates</c>.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The plan is pure configuration data — no behavior lives on it. Resolution is
/// performed by <c>IIncidentResponsePlanResolver</c>; gate overlay is performed
/// by the <c>ChangeProposalOrchestrator</c> at evaluation time so the audit
/// trail captures both the original proposal shape and the incident-driven
/// additions.
/// </para>
/// <para>
/// <see cref="AutonomyTierOverride"/> uses the string name of the
/// <c>Domain.AI.Governance.AutonomyLevel</c> enum (<c>Restricted</c>,
/// <c>Supervised</c>, <c>Autonomous</c>). Storing the override as a string
/// keeps Domain.Common free of a reference to Domain.AI; the validator parses
/// and rejects unknown values at boot so consumers cannot silently typo a tier.
/// </para>
/// </remarks>
public sealed record IncidentResponsePlan
{
    /// <summary>
    /// Human-readable identifier for the plan. Must be unique across all plans
    /// in <c>IncidentResponsePlanConfig.Plans</c>. Surfaces in audit lines as
    /// the reason for any incident-driven gate overlay so reviewers see which
    /// plan added the extra gates.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The incident type token this plan answers to. Matched
    /// case-insensitively against <c>IIncidentContext.CurrentIncidentType</c>.
    /// Empty / null means "this plan is the default" — see
    /// <c>IncidentResponsePlanConfig.DefaultPlanName</c> for fallback semantics.
    /// </summary>
    public required string IncidentType { get; init; }

    /// <summary>
    /// Ordered list of skill ids to activate on the agent while the incident is
    /// active. Empty list means no skill activation; the agent runs with its
    /// existing skill set. The harness's skill-activation pipeline consumes
    /// this surface — the plan itself enforces no ordering or prerequisite
    /// invariants.
    /// </summary>
    public IReadOnlyList<string> SkillSet { get; init; } = [];

    /// <summary>
    /// Optional autonomy-tier override applied for the duration of the
    /// incident. Must match an <c>AutonomyLevel</c> enum name
    /// (<c>Restricted</c>, <c>Supervised</c>, <c>Autonomous</c>) when non-null.
    /// Null means "do not override the agent's normal tier".
    /// </summary>
    /// <remarks>
    /// Typical pattern: a tenant running an Autonomous agent under
    /// <c>DataExfiltrationSuspected</c> forces the override to
    /// <c>Restricted</c> so every action requires approval until the incident
    /// clears. The validator rejects unknown values at boot.
    /// </remarks>
    public string? AutonomyTierOverride { get; init; }

    /// <summary>
    /// Additional required-gate keys overlaid on any <c>ChangeProposal</c>
    /// submitted while this plan is active. The orchestrator overlays at
    /// evaluation time only — the proposal's stored
    /// <c>RequiredGates</c> is never mutated. Duplicates of gates already in
    /// the proposal's required list are silently de-duplicated; ordering puts
    /// existing required gates first, then incident-added gates in declared
    /// order.
    /// </summary>
    public IReadOnlyList<string> AdditionalRequiredGates { get; init; } = [];
}
