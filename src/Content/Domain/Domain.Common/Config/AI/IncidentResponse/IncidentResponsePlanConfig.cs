namespace Domain.Common.Config.AI.IncidentResponse;

/// <summary>
/// Root binding for incident-response policy. Sits under
/// <c>AppConfig:AI:IncidentResponse</c>. Empty by default — the resolver
/// returns <c>null</c> for every incident type so the host has zero behavioural
/// change until a consumer declares plans.
/// </summary>
/// <remarks>
/// <para>
/// Hot reload is supported: <c>IIncidentResponsePlanResolver</c> reads through
/// <c>IOptionsMonitor&lt;AppConfig&gt;.CurrentValue</c> on every resolve, so
/// config edits at runtime take effect on the next lookup without restarting
/// the host.
/// </para>
/// </remarks>
public sealed class IncidentResponsePlanConfig
{
    /// <summary>
    /// Declared incident-response plans. Looked up by
    /// <c>IncidentResponsePlan.IncidentType</c> (case-insensitive).
    /// </summary>
    /// <remarks>
    /// The startup validator refuses to boot if two plans share the same
    /// <c>Name</c> — duplicate names make the audit trail ambiguous about
    /// which plan was actually applied. Two plans sharing the same
    /// <c>IncidentType</c> are also rejected; the resolver picks the first
    /// match deterministically but the duplicate is almost certainly a config
    /// mistake.
    /// </remarks>
    public List<IncidentResponsePlan> Plans { get; set; } = [];

    /// <summary>
    /// Optional plan name used as fallback when the active incident type does
    /// not match any declared plan. Null means "no fallback" — the resolver
    /// returns <c>null</c> for unmatched incident types so the orchestrator
    /// behaves as if no incident were active.
    /// </summary>
    /// <remarks>
    /// Typical pattern: a consumer with a permissive "everything else" plan
    /// (e.g. <c>StandardOps</c>) sets this to that plan's name so any
    /// unrecognised incident still routes through the baseline gates. The
    /// validator rejects a non-null value that does not match any declared
    /// plan's <c>Name</c>.
    /// </remarks>
    public string? DefaultPlanName { get; set; }
}
