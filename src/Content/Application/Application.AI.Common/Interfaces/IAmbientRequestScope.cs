namespace Application.AI.Common.Interfaces;

/// <summary>
/// Bridges the per-request DI scope into long-lived, singleton-cached components (such as an
/// agent's <c>AIContextProvider</c>s) that would otherwise have no access to the current
/// request's services.
/// </summary>
/// <remarks>
/// <para>
/// The harness caches agents per conversation as singletons (see <c>IAgentConversationCache</c>),
/// so any <c>AIContextProvider</c> attached to a cached agent lives for the whole application and
/// is shared across requests and tenants. Such a provider must therefore <strong>never capture</strong>
/// scoped, tenant-aware services (e.g. <c>IKnowledgeMemory</c>) at construction — doing so would
/// freeze it on the first request's scope and leak state across requests.
/// </para>
/// <para>
/// Instead, the request handler establishes the current request scope via <see cref="BeginScope"/>
/// for the duration of the agent invocation. Because the backing store is an
/// <see cref="System.Threading.AsyncLocal{T}"/>, <see cref="Current"/> flows down the async call
/// chain into the provider, which resolves the scoped services it needs <em>per invocation</em>
/// from the correct request scope.
/// </para>
/// </remarks>
public interface IAmbientRequestScope
{
    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> for the request currently in flight on this async
    /// execution context, or <see langword="null"/> when no request scope has been established
    /// (e.g. background work, or a code path that does not run inside an agent turn).
    /// </summary>
    IServiceProvider? Current { get; }

    /// <summary>
    /// Establishes <paramref name="requestServices"/> as the ambient request scope for the current
    /// async execution context until the returned token is disposed. Restores the previous value on
    /// disposal so nested scopes compose correctly.
    /// </summary>
    /// <param name="requestServices">The current request's scoped service provider.</param>
    /// <returns>A token that clears the ambient scope when disposed.</returns>
    IDisposable BeginScope(IServiceProvider requestServices);
}
