namespace Application.AI.Common.Interfaces.IncidentResponse;

/// <summary>
/// Ambient context exposing the currently active incident type, if any. Set
/// once per request by the Presentation layer (typically a middleware or hub
/// filter) when an incident-tracking system reports the host is operating
/// under an active incident. Read by services that need to vary their
/// behaviour while an incident is active — notably the
/// <c>ChangeProposalOrchestrator</c>, which consults
/// <see cref="IIncidentResponsePlanResolver"/> to overlay additional
/// required-gates onto in-flight proposals.
/// </summary>
/// <remarks>
/// <para>
/// Implementations flow the active incident type through async continuations
/// and child DI scopes via <see cref="System.Threading.AsyncLocal{T}"/> — the
/// same pattern the harness uses for its knowledge-scope identity. This is
/// deliberate: the orchestrator dispatch and gate evaluations may run on a
/// background <c>IHostedService</c> task after the originating request scope
/// has been disposed, and ambient flow is the only way to carry the incident
/// type into those continuations without threading it through every interface.
/// </para>
/// <para>
/// The interface is intentionally minimal — set, read, clear. Higher-level
/// concerns (mapping a Sentry / PagerDuty alert to an incident type, deciding
/// when to clear the incident) live in consumer-supplied glue at the
/// Presentation layer.
/// </para>
/// </remarks>
public interface IIncidentContext
{
    /// <summary>
    /// The active incident type, or <c>null</c> when no incident is active.
    /// Reading is free — every consumer that cares about the incident state
    /// reads this property and then consults
    /// <see cref="IIncidentResponsePlanResolver"/>.
    /// </summary>
    string? CurrentIncidentType { get; }

    /// <summary>
    /// Set the active incident for the current async execution context. The
    /// value flows into child scopes and background continuations through
    /// <see cref="System.Threading.AsyncLocal{T}"/>. Passing <c>null</c> clears
    /// the incident — equivalent to a follow-up "incident resolved" report.
    /// </summary>
    /// <param name="incidentType">
    /// The incident type token, or <c>null</c> to clear. Whitespace is
    /// normalised to <c>null</c> so empty strings don't accidentally activate
    /// a non-existent plan.
    /// </param>
    void Set(string? incidentType);
}
