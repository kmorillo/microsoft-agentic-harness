using Application.AI.Common.Interfaces.KnowledgeGraph;

namespace Presentation.AgentHub.Middleware;

/// <summary>
/// Establishes the per-request knowledge scope (user + tenant) from the authenticated
/// <see cref="HttpContext.User"/> for every HTTP request. This is the chokepoint that lets
/// cross-session memory and graph-store isolation attribute work to the correct user/tenant,
/// covering controllers and the AG-UI minimal-API endpoint alike.
/// </summary>
/// <remarks>
/// <para>
/// Must run <em>after</em> <c>UseAuthentication</c> so <see cref="HttpContext.User"/> is populated.
/// <see cref="IKnowledgeScopeWriter"/> is resolved per request (method injection), so the scope is
/// set on the same scoped instance the downstream MediatR handler and graph-store decorators read.
/// </para>
/// <para>
/// Unauthenticated requests (anonymous endpoints, health probes, the dev auth bypass without an
/// <c>oid</c>) leave the scope at its configured default — the writer is only invoked when a user id
/// is present.
/// </para>
/// </remarks>
public sealed class KnowledgeScopeMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeScopeMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public KnowledgeScopeMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    /// <summary>
    /// Sets the knowledge scope from the authenticated principal, then invokes the rest of the pipeline.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="scopeWriter">The request-scoped knowledge scope writer.</param>
    public async Task InvokeAsync(HttpContext context, IKnowledgeScopeWriter scopeWriter)
    {
        KnowledgeScopeInitializer.Apply(context.User, scopeWriter);
        await _next(context);
    }
}
