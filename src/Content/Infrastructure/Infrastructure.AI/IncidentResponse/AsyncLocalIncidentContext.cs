using Application.AI.Common.Interfaces.IncidentResponse;

namespace Infrastructure.AI.IncidentResponse;

/// <summary>
/// Default <see cref="IIncidentContext"/>: stores the active incident type in
/// an <see cref="AsyncLocal{T}"/> so it flows into child scopes and background
/// continuations without explicit plumbing.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a Singleton in DI even though the value it returns is per
/// async context. This is the standard <see cref="AsyncLocal{T}"/> pattern —
/// the holder is shared, the slot is per-context. Mirrors
/// <c>KnowledgeScopeAccessor</c> for the same reason: the orchestrator's
/// background dispatch runs after the originating request scope is gone, so a
/// per-scope holder would lose the incident value.
/// </para>
/// <para>
/// Whitespace-only inputs to <see cref="Set"/> are normalised to <c>null</c>
/// so a caller that defensively passes <c>incidentType ?? string.Empty</c>
/// can't accidentally activate a "default-by-empty" plan.
/// </para>
/// </remarks>
public sealed class AsyncLocalIncidentContext : IIncidentContext
{
    private static readonly AsyncLocal<string?> s_incidentType = new();

    /// <inheritdoc />
    public string? CurrentIncidentType => s_incidentType.Value;

    /// <inheritdoc />
    public void Set(string? incidentType)
    {
        s_incidentType.Value = string.IsNullOrWhiteSpace(incidentType) ? null : incidentType;
    }
}
