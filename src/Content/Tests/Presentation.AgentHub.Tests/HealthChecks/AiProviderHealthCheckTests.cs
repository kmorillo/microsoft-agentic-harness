using Application.AI.Common.Models;
using Domain.Common.Config.AI;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Presentation.AgentHub.HealthChecks;
using Presentation.AgentHub.Tests.Fakes;
using Xunit;

namespace Presentation.AgentHub.Tests.HealthChecks;

public sealed class AiProviderHealthCheckTests
{
    private static HealthCheckContext ContextWithFailureStatus(HealthStatus failureStatus) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "ai_provider",
                _ => null!,
                failureStatus,
                tags: ["ai"]),
        };

    [Fact]
    public async Task CheckHealthAsync_WhenConfigured_ReturnsHealthy()
    {
        var factory = new FakeChatClientFactory
        {
            ProviderStatus = new AiProviderStatus(
                AIAgentFrameworkClientType.Anthropic, "claude-sonnet-4-6", IsConfigured: true, MissingSettings: []),
        };
        var check = new AiProviderHealthCheck(factory);

        var result = await check.CheckHealthAsync(ContextWithFailureStatus(HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenNotConfigured_ReturnsRegistrationFailureStatusWithMissingSettings()
    {
        var missing = new[] { "AppConfig:AI:AgentFramework:Endpoint", "AppConfig:AI:AgentFramework:ApiKey" };
        var factory = new FakeChatClientFactory
        {
            ProviderStatus = new AiProviderStatus(
                AIAgentFrameworkClientType.Anthropic, "claude-sonnet-4-6", IsConfigured: false, MissingSettings: missing),
        };
        var check = new AiProviderHealthCheck(factory);

        var result = await check.CheckHealthAsync(ContextWithFailureStatus(HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Endpoint").And.Contain("ApiKey");
        result.Data.Should().ContainKey("missingSettings");
    }
}
