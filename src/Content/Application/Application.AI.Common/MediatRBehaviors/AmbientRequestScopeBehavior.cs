using Application.AI.Common.Interfaces;
using MediatR;

namespace Application.AI.Common.MediatRBehaviors;

/// <summary>
/// Establishes the current request's DI scope as the ambient request scope (<see cref="IAmbientRequestScope"/>)
/// for the duration of the MediatR pipeline. This lets singleton-cached agents' <c>AIContextProvider</c>s —
/// e.g. <c>KnowledgeMemoryContextProvider</c> — resolve request-scoped, tenant-aware services
/// (such as <c>IKnowledgeMemory</c>) per invocation, for the correct user, even though the agent itself
/// outlives any single request.
/// </summary>
/// <remarks>
/// Registered as the outermost pipeline behavior so the scope is established before any handler or other
/// behavior runs. It applies to every request: this covers single chat turns, orchestrated tasks, and the
/// child-scope subtask dispatches an orchestrator issues (each of which is its own MediatR request and so
/// re-establishes its own scope here). The cost for non-agent requests is a single
/// <see cref="System.Threading.AsyncLocal{T}"/> set/restore — negligible.
/// </remarks>
public sealed class AmbientRequestScopeBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly IAmbientRequestScope _ambientScope;
    private readonly IServiceProvider _requestServices;

    /// <summary>
    /// Initializes a new instance of the <see cref="AmbientRequestScopeBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="ambientScope">The ambient request-scope bridge.</param>
    /// <param name="requestServices">
    /// The service provider that resolved this behavior. Because MediatR resolves the pipeline from the
    /// request scope, this is the current request's scoped provider.
    /// </param>
    public AmbientRequestScopeBehavior(IAmbientRequestScope ambientScope, IServiceProvider requestServices)
    {
        _ambientScope = ambientScope;
        _requestServices = requestServices;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        using (_ambientScope.BeginScope(_requestServices))
        {
            return await next();
        }
    }
}
