using Application.AI.Common.Interfaces.SkillTraining;
using Application.AI.Common.Services.SkillTraining;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.AI.Common.Extensions;

/// <summary>
/// Registers the skill-training subsystem's pure components into DI. Composition root
/// (Presentation) calls this from <c>AddApplicationAIDependencies</c>; CQRS handlers and
/// FluentValidation validators are picked up automatically by the existing assembly scans.
/// </summary>
/// <remarks>
/// <para>
/// Default-registered services:
/// <list type="bullet">
/// <item><see cref="PatchApplier"/> — concrete pure service for applying patches.</item>
/// <item><see cref="IGateEvaluator"/> → <see cref="GateEvaluator"/>.</item>
/// <item><see cref="IPatchAggregator"/> → <see cref="PatchAggregator"/>.</item>
/// <item><see cref="IEditSelector"/> → <see cref="TopKEditSelector"/>.</item>
/// <item><see cref="EditableSurfaceRegistry"/> — code-owned editable-surface fence allowlist.</item>
/// <item><see cref="HarnessPatchValidator"/> — concrete fence (no interface seam, by design).</item>
/// <item><see cref="ISkillTrainingCheckpointStore"/> → <see cref="InMemorySkillTrainingCheckpointStore"/>.</item>
/// <item><see cref="IPatchProposer"/> → <see cref="NotConfiguredPatchProposer"/> (fail-fast default).</item>
/// <item><see cref="IRolloutRunner"/> → <see cref="NotConfiguredRolloutRunner"/> (fail-fast default).</item>
/// </list>
/// </para>
/// <para>
/// Template consumers replace the two <c>NotConfigured</c> defaults with agent-backed
/// implementations (typically in Infrastructure.AI). The default registrations use
/// <c>TryAddSingleton</c> so replacements added before this call are preserved.
/// </para>
/// </remarks>
public static class SkillTrainingDependencyInjection
{
    /// <summary>Registers the skill-training subsystem's services.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSkillTrainingDependencies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<PatchApplier>();
        services.TryAddSingleton<IGateEvaluator, GateEvaluator>();
        services.TryAddSingleton<IPatchAggregator, PatchAggregator>();
        services.TryAddSingleton<IEditSelector, TopKEditSelector>();
        services.TryAddSingleton<EditableSurfaceRegistry>();
        services.TryAddSingleton<HarnessPatchValidator>();
        services.TryAddSingleton<ISkillTrainingCheckpointStore, InMemorySkillTrainingCheckpointStore>();
        services.TryAddSingleton<IPatchProposer, NotConfiguredPatchProposer>();
        services.TryAddSingleton<IRolloutRunner, NotConfiguredRolloutRunner>();

        return services;
    }
}
