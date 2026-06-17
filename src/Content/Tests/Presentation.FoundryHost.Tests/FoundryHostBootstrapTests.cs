using Domain.AI.Agents;
using FluentAssertions;
using Presentation.FoundryHost;
using Xunit;

namespace Presentation.FoundryHost.Tests;

/// <summary>
/// Unit tests for <see cref="FoundryHostBootstrap"/> — the environment-to-config translation and
/// agent/skill selection that determine how the hosted container is wired and which agent it serves.
/// </summary>
public sealed class FoundryHostBootstrapTests
{
    private static Func<string, string?> Env(params (string Key, string? Value)[] entries)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            map[key] = value;
        }

        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    [Fact]
    public void BuildConfigOverrides_MapsFoundryEndpointAndDeployment_ToAppConfigKeys()
    {
        var overrides = FoundryHostBootstrap.BuildConfigOverrides(Env(
            ("FOUNDRY_PROJECT_ENDPOINT", "https://proj.services.ai.azure.com/api/projects/p1"),
            ("AZURE_AI_MODEL_DEPLOYMENT_NAME", "gpt-4o-mini")));

        overrides["AppConfig__AI__AIFoundry__ProjectEndpoint"]
            .Should().Be("https://proj.services.ai.azure.com/api/projects/p1");
        overrides["AppConfig__AI__AgentFramework__DefaultDeployment"].Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void BuildConfigOverrides_WithAppInsights_EnablesAzureMonitorExporter()
    {
        var overrides = FoundryHostBootstrap.BuildConfigOverrides(Env(
            ("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=abc;IngestionEndpoint=https://x")));

        overrides["AppConfig__Observability__Exporters__AzureMonitor__ConnectionString"]
            .Should().Be("InstrumentationKey=abc;IngestionEndpoint=https://x");
        overrides["AppConfig__Observability__Exporters__AzureMonitor__Enabled"].Should().Be("true");
    }

    [Fact]
    public void BuildConfigOverrides_WithNoFoundryEnvironment_ReturnsEmpty()
    {
        var overrides = FoundryHostBootstrap.BuildConfigOverrides(Env());

        overrides.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildConfigOverrides_IgnoresBlankSourceValues(string blank)
    {
        var overrides = FoundryHostBootstrap.BuildConfigOverrides(Env(
            ("FOUNDRY_PROJECT_ENDPOINT", blank),
            ("APPLICATIONINSIGHTS_CONNECTION_STRING", blank)));

        overrides.Should().BeEmpty();
    }

    [Fact]
    public void ResolveAgentId_WhenUnset_ReturnsDefault()
    {
        FoundryHostBootstrap.ResolveAgentId(Env()).Should().Be(FoundryHostBootstrap.DefaultAgentId);
        FoundryHostBootstrap.ResolveAgentId(Env(("FOUNDRY_AGENT_ID", "  "))).Should().Be("default");
    }

    [Fact]
    public void ResolveAgentId_WhenSet_ReturnsConfiguredId()
    {
        FoundryHostBootstrap.ResolveAgentId(Env(("FOUNDRY_AGENT_ID", "research"))).Should().Be("research");
    }

    [Fact]
    public void ResolveSkillIds_WithDeclaredSkills_ReturnsThem()
    {
        var definition = new AgentDefinition
        {
            Id = "orchestrator",
            Name = "Orchestrator",
            Skills = ["planner", "researcher"]
        };

        FoundryHostBootstrap.ResolveSkillIds(definition).Should().Equal("planner", "researcher");
    }

    [Fact]
    public void ResolveSkillIds_WithNoDeclaredSkills_FallsBackToAgentId()
    {
        var definition = new AgentDefinition { Id = "default", Name = "Default" };

        FoundryHostBootstrap.ResolveSkillIds(definition).Should().Equal("default");
    }
}
