using System.Security.Claims;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Presentation.AgentHub.Extensions;

namespace Presentation.AgentHub.Middleware;

/// <summary>
/// Maps an authenticated <see cref="ClaimsPrincipal"/> onto the request's
/// <see cref="IKnowledgeScopeWriter"/>. Shared by the HTTP <see cref="KnowledgeScopeMiddleware"/>
/// and the SignalR <c>KnowledgeScopeHubFilter</c> so both transports establish scope identically.
/// </summary>
public static class KnowledgeScopeInitializer
{
    /// <summary>
    /// Sets the knowledge scope from <paramref name="user"/> when a user id is present; otherwise
    /// leaves scope at its configured default (anonymous / health probes / dev auth without an oid).
    /// </summary>
    /// <param name="user">The authenticated principal, or <c>null</c>.</param>
    /// <param name="scopeWriter">The request-scoped scope writer.</param>
    public static void Apply(ClaimsPrincipal? user, IKnowledgeScopeWriter scopeWriter)
    {
        ArgumentNullException.ThrowIfNull(scopeWriter);

        var userId = user?.GetUserIdOrNull();
        if (userId is null)
            return;

        // Set only user + tenant: that is what memory namespacing keys on. Dataset properties are
        // left unset so they keep falling back to the configured defaults; dataset-level scope is a
        // later concern (the graph-store isolation decorators), not part of conversation-memory scope.
        scopeWriter.SetScope(userId: userId, tenantId: user!.GetTenantId());
    }
}
