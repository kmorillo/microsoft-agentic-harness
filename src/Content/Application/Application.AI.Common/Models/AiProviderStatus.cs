using Domain.Common.Config.AI;

namespace Application.AI.Common.Models;

/// <summary>
/// Snapshot of whether the active AI provider is ready to serve agent turns.
/// Returned by <see cref="Interfaces.IChatClientFactory.GetProviderStatus"/> and consumed by the
/// config-status endpoint, the AI health check, and startup diagnostics so a misconfiguration is
/// surfaced once and reported everywhere.
/// </summary>
/// <remarks>
/// Carries only configuration <em>names</em> and booleans — never secret values — so it is safe to
/// expose over HTTP and in health reports.
/// </remarks>
/// <param name="ClientType">The active default client type from <c>AppConfig.AI.AgentFramework.ClientType</c>.</param>
/// <param name="DefaultDeployment">The configured default deployment/model name.</param>
/// <param name="IsConfigured">
/// <c>true</c> when the active provider has everything it needs to create a chat client.
/// </param>
/// <param name="MissingSettings">
/// The configuration keys that must be supplied before the provider can serve requests
/// (e.g. <c>AppConfig:AI:AgentFramework:ApiKey</c>). Empty when <see cref="IsConfigured"/> is <c>true</c>.
/// </param>
public sealed record AiProviderStatus(
    AIAgentFrameworkClientType ClientType,
    string DefaultDeployment,
    bool IsConfigured,
    IReadOnlyList<string> MissingSettings);
