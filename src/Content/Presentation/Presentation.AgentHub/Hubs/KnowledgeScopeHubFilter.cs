using Application.AI.Common.Interfaces.KnowledgeGraph;
using Microsoft.AspNetCore.SignalR;
using Presentation.AgentHub.Middleware;

namespace Presentation.AgentHub.Hubs;

/// <summary>
/// SignalR hub filter that establishes the per-invocation knowledge scope (user + tenant) from the
/// authenticated <see cref="HubCallerContext.User"/> before every hub method runs. The HTTP
/// <c>KnowledgeScopeMiddleware</c> does not cover hub method invocations (they run on their own DI
/// scope with no HTTP request), so this is the equivalent chokepoint for the SignalR transport.
/// </summary>
/// <remarks>
/// <see cref="IKnowledgeScopeWriter"/> is resolved from <see cref="HubInvocationContext.ServiceProvider"/>
/// — the same per-invocation scope from which the hub's orchestrator and the downstream MediatR
/// handler/graph-store decorators resolve <see cref="IKnowledgeScope"/> — so the value set here is the
/// value they observe.
/// </remarks>
public sealed class KnowledgeScopeHubFilter : IHubFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (invocationContext.ServiceProvider.GetService(typeof(IKnowledgeScopeWriter)) is IKnowledgeScopeWriter scopeWriter)
            KnowledgeScopeInitializer.Apply(invocationContext.Context.User, scopeWriter);

        return next(invocationContext);
    }
}
