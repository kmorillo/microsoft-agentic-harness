using Application.AI.Common.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Presentation.AgentHub.HealthChecks;

/// <summary>
/// Health check reporting whether the active AI provider is configured to serve agent turns.
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Healthy"/> when configured. When it is not, it reports the
/// registration's failure status (Degraded, not Unhealthy) because the host still serves the
/// dashboard, telemetry, and Echo-mode agents — only live LLM turns are unavailable. The missing
/// configuration keys are returned in the result data so <c>/health/ai</c> is directly actionable.
/// </remarks>
public sealed class AiProviderHealthCheck : IHealthCheck
{
    private readonly IChatClientFactory _chatClientFactory;

    /// <summary>Initializes the check with the chat-client factory it queries.</summary>
    public AiProviderHealthCheck(IChatClientFactory chatClientFactory) =>
        _chatClientFactory = chatClientFactory;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = _chatClientFactory.GetProviderStatus();

        var data = new Dictionary<string, object>
        {
            ["clientType"] = status.ClientType.ToString(),
            ["defaultDeployment"] = status.DefaultDeployment,
            ["missingSettings"] = status.MissingSettings,
        };

        if (status.IsConfigured)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"AI provider '{status.ClientType}' is configured.", data));
        }

        return Task.FromResult(new HealthCheckResult(
            context.Registration.FailureStatus,
            $"AI provider '{status.ClientType}' is not configured. " +
            $"Missing: {string.Join(", ", status.MissingSettings)}.",
            exception: null,
            data: data));
    }
}
