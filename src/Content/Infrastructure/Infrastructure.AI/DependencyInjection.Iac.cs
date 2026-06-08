using Application.AI.Common.Interfaces.Changes;
using Domain.AI.Changes;
using Infrastructure.AI.Iac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI;

public static partial class DependencyInjection
{
    /// <summary>
    /// Swaps the fail-loud <c>NotConfiguredValidator</c> placeholder registered for
    /// <see cref="ChangeTargetKind.IacDeployment"/> (inside
    /// <see cref="RegisterChangesServices"/>) for the real
    /// <see cref="IacChangeProposalValidator"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The placeholder is registered later in the composition order than the IaC
    /// skill tools, so this swap MUST run after <see cref="RegisterChangesServices"/>.
    /// It removes every keyed <see cref="IChangeProposalValidator"/> descriptor for
    /// the IaC target kind (there is exactly one — the placeholder — but the loop is
    /// defensive against future additions) and registers the real validator under the
    /// same key. The <c>SelfValidationGate</c> then enumerates only the real validator
    /// for IaC proposals.
    /// </para>
    /// <para>
    /// Registered as a keyed singleton resolving <see cref="IServiceProvider"/> so the
    /// validator can look up the backend <c>IIacGenerator</c> by the target's backend
    /// string at evaluation time.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to mutate.</param>
    private static void RegisterIacValidator(IServiceCollection services)
    {
        var placeholders = services
            .Where(d => d.ServiceType == typeof(IChangeProposalValidator)
                        && d.IsKeyedService
                        && Equals(d.ServiceKey, ChangeTargetKind.IacDeployment))
            .ToList();

        foreach (var descriptor in placeholders)
        {
            services.Remove(descriptor);
        }

        services.AddKeyedSingleton<IChangeProposalValidator>(
            ChangeTargetKind.IacDeployment,
            static (sp, _) => new IacChangeProposalValidator(
                sp,
                sp.GetRequiredService<ILogger<IacChangeProposalValidator>>()));
    }
}
