using Domain.Common.Config.AI.IncidentResponse;

namespace Application.AI.Common.Interfaces.IncidentResponse;

/// <summary>
/// Lookup service that maps an incident type token to the configured
/// <see cref="IncidentResponsePlan"/>. Consulted by the
/// <c>ChangeProposalOrchestrator</c> (and by skill / autonomy wiring) when the
/// host reports an active incident.
/// </summary>
/// <remarks>
/// <para>
/// Resolution semantics:
/// </para>
/// <list type="number">
///   <item><description>
///     Plans are matched by <see cref="IncidentResponsePlan.IncidentType"/>
///     case-insensitively. The first matching plan wins; the boot validator
///     refuses to start with duplicate incident-type entries so the
///     "first match" rule is deterministic in well-configured hosts.
///   </description></item>
///   <item><description>
///     If no plan matches and a fallback
///     (<c>IncidentResponsePlanConfig.DefaultPlanName</c>) is configured, the
///     resolver returns that plan.
///   </description></item>
///   <item><description>
///     If neither a match nor a default exists, the resolver returns
///     <c>null</c> — callers treat null as "no incident behaviour applies".
///   </description></item>
/// </list>
/// <para>
/// The resolver reads through <c>IOptionsMonitor&lt;AppConfig&gt;.CurrentValue</c>
/// on every call so hot-reloaded config takes effect on the next lookup.
/// </para>
/// </remarks>
public interface IIncidentResponsePlanResolver
{
    /// <summary>
    /// Resolve the plan for an incident type token.
    /// </summary>
    /// <param name="incidentType">
    /// The active incident type. May be null or whitespace — in that case the
    /// resolver returns the default plan if one is configured, otherwise
    /// <c>null</c>.
    /// </param>
    /// <returns>
    /// The matching plan, the configured default plan, or <c>null</c> when
    /// neither exists.
    /// </returns>
    IncidentResponsePlan? ResolveFor(string? incidentType);
}
