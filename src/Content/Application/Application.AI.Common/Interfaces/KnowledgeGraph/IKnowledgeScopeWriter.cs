namespace Application.AI.Common.Interfaces.KnowledgeGraph;

/// <summary>
/// Write side of the knowledge scope. Lets the host establish the authenticated user,
/// tenant, and dataset for the current request exactly once — typically from an HTTP
/// middleware or a SignalR hub filter at the entry point — without exposing a mutator on
/// the read-only <see cref="IKnowledgeScope"/> that the rest of the harness consumes.
/// </summary>
/// <remarks>
/// <para>
/// Registered <c>Scoped</c> and backed by the <em>same instance</em> as <see cref="IKnowledgeScope"/>,
/// so a value set here through the request's scope is observed by every scoped consumer in that
/// request (memory service, graph-store decorators, audit). Until a host calls <see cref="SetScope"/>,
/// scope falls back to configuration defaults and all callers share a single default tenant — which is
/// why cross-session memory (<c>AppConfig.AI.KnowledgeBridge.Enabled</c>) is opt-in.
/// </para>
/// </remarks>
public interface IKnowledgeScopeWriter
{
    /// <summary>
    /// Sets the knowledge scope properties for the current request. Call once per request,
    /// from authenticated entry-point middleware or a hub filter.
    /// </summary>
    /// <param name="userId">The authenticated user ID (e.g. the Azure AD <c>oid</c> claim).</param>
    /// <param name="tenantId">The tenant ID (e.g. the Azure AD <c>tid</c> claim); overrides the config default.</param>
    /// <param name="datasetId">The dataset ID; overrides the config default.</param>
    /// <param name="datasetName">The dataset display name.</param>
    /// <param name="datasetOwnerId">The dataset owner's user ID.</param>
    void SetScope(
        string? userId = null,
        string? tenantId = null,
        string? datasetId = null,
        string? datasetName = null,
        string? datasetOwnerId = null);
}
