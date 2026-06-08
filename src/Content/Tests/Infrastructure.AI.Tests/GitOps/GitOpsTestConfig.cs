using Domain.Common.Config;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.GitOps;
using Domain.Common.Config.AI.MCP;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Shared builders for the GitOps test suite. Produces an
/// <see cref="IOptionsMonitor{AppConfig}"/> whose <c>AI.GitOps</c> section is
/// configured exactly the way each scenario needs, so the controllers, clients,
/// dispatcher, validator, and DI tests can all assert against a known config.
/// </summary>
internal static class GitOpsTestConfig
{
    /// <summary>The default K8sGPT MCP server key used across the suite.</summary>
    public const string K8sGptServerName = "k8sgpt";

    /// <summary>
    /// Builds an <see cref="AppConfig"/> with a fully-valid GitOps section for the
    /// supplied active controller, including a matching K8sGPT MCP server entry.
    /// </summary>
    public static AppConfig ValidAppConfig(
        string activeController = "flux",
        string fluxBaseUrl = "https://flux.example.com",
        string argoCdBaseUrl = "https://argocd.example.com",
        string remediationRepoUrl = "https://github.com/example/cluster-config.git",
        string remediationBranch = "main")
    {
        var appConfig = new AppConfig
        {
            AI = new AIConfig
            {
                GitOps = new GitOpsConfig
                {
                    Enabled = true,
                    ActiveController = activeController,
                    K8sGptMcpServerName = K8sGptServerName,
                    FluxApiBaseUrl = fluxBaseUrl,
                    ArgoCdApiBaseUrl = argoCdBaseUrl,
                    RemediationRepoUrl = remediationRepoUrl,
                    RemediationBranch = remediationBranch
                }
            }
        };

        appConfig.AI.McpServers.Servers[K8sGptServerName] = new McpServerDefinition();
        return appConfig;
    }

    /// <summary>Wraps an <see cref="AppConfig"/> in a Moq-backed monitor.</summary>
    public static IOptionsMonitor<AppConfig> Monitor(AppConfig appConfig)
        => Mock.Of<IOptionsMonitor<AppConfig>>(o => o.CurrentValue == appConfig);

    /// <summary>Convenience: a monitor over a valid config for the active controller.</summary>
    public static IOptionsMonitor<AppConfig> ValidMonitor(string activeController = "flux")
        => Monitor(ValidAppConfig(activeController));
}
