using Application.AI.Common.Interfaces.IncidentResponse;
using Infrastructure.AI.IncidentResponse;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.AI;

public static partial class DependencyInjection
{
    /// <summary>
    /// Registers the incident-response plan subsystem (PR-5): the
    /// <see cref="IIncidentContext"/> ambient holder, the
    /// <see cref="IIncidentResponsePlanResolver"/> default implementation, and
    /// the boot-time validator that refuses to start the host with internally
    /// inconsistent plan config.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All registrations are unconditional — the resolver returns <c>null</c>
    /// and the orchestrator overlay does nothing when no plans are configured,
    /// so the subsystem is inert until a consumer declares plans under
    /// <c>AppConfig.AI.IncidentResponse</c>.
    /// </para>
    /// <para>
    /// The ambient context is registered as a Singleton — its storage slot is
    /// per <see cref="System.Threading.AsyncLocal{T}"/> context, so a shared
    /// holder is correct and required (a Scoped holder would lose the value
    /// when the orchestrator's background dispatch ran after the originating
    /// request scope was disposed).
    /// </para>
    /// </remarks>
    private static void RegisterIncidentResponseServices(IServiceCollection services)
    {
        services.AddSingleton<IIncidentContext, AsyncLocalIncidentContext>();
        services.AddSingleton<IIncidentResponsePlanResolver, IncidentResponsePlanResolver>();
        services.AddHostedService<IncidentResponsePlanValidator>();
    }
}
