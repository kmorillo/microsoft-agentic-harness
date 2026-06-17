using Domain.AI.Agents;

namespace Presentation.FoundryHost;

/// <summary>
/// Pure bootstrap logic for the Foundry hosted-agent container, factored out of
/// <see cref="Program"/> so the bug-prone parts — environment-variable translation and
/// agent/skill selection — are unit-testable without standing up the web host.
/// </summary>
internal static class FoundryHostBootstrap
{
    /// <summary>Default agent id served when <c>FOUNDRY_AGENT_ID</c> is not set.</summary>
    internal const string DefaultAgentId = "default";

    /// <summary>
    /// Computes the harness configuration overrides implied by Foundry's runtime-injected
    /// environment variables. Returns the <c>AppConfig__...</c> keys that should be set, leaving the
    /// actual <see cref="Environment"/> mutation to the caller so this stays pure and testable.
    /// </summary>
    /// <param name="readEnv">Reads an environment variable by name (returns <c>null</c> when unset).</param>
    /// <remarks>
    /// Only non-empty source values produce overrides. A present
    /// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> additionally enables the Azure Monitor exporter
    /// so harness traces and metrics flow to the injected sink.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> BuildConfigOverrides(Func<string, string?> readEnv)
    {
        ArgumentNullException.ThrowIfNull(readEnv);

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

        AddIfPresent(overrides, readEnv, "FOUNDRY_PROJECT_ENDPOINT", "AppConfig__AI__AIFoundry__ProjectEndpoint");
        AddIfPresent(overrides, readEnv, "AZURE_AI_MODEL_DEPLOYMENT_NAME", "AppConfig__AI__AgentFramework__DefaultDeployment");

        var appInsights = readEnv("APPLICATIONINSIGHTS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            overrides["AppConfig__Observability__Exporters__AzureMonitor__ConnectionString"] = appInsights;
            overrides["AppConfig__Observability__Exporters__AzureMonitor__Enabled"] = "true";
        }

        return overrides;
    }

    /// <summary>
    /// Resolves which agent id this deployment exposes: <c>FOUNDRY_AGENT_ID</c> when set,
    /// otherwise <see cref="DefaultAgentId"/>.
    /// </summary>
    internal static string ResolveAgentId(Func<string, string?> readEnv)
    {
        ArgumentNullException.ThrowIfNull(readEnv);
        var configured = readEnv("FOUNDRY_AGENT_ID");
        return string.IsNullOrWhiteSpace(configured) ? DefaultAgentId : configured;
    }

    /// <summary>
    /// Returns the skill ids that compose an agent: the manifest's declared skills, or the agent id
    /// itself as a single skill when the manifest declares none (per <see cref="AgentDefinition"/>).
    /// </summary>
    internal static IReadOnlyList<string> ResolveSkillIds(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Skills.Count > 0 ? definition.Skills : [definition.Id];
    }

    private static void AddIfPresent(
        IDictionary<string, string> overrides,
        Func<string, string?> readEnv,
        string source,
        string target)
    {
        var value = readEnv(source);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[target] = value;
        }
    }
}
