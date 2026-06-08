using Application.AI.Common.Interfaces.GitOps;
using Application.AI.Common.Interfaces.Tools;
using Domain.Common.Config;
using Infrastructure.AI.GitOps;
using Infrastructure.AI.GitOps.ArgoCd;
using Infrastructure.AI.GitOps.Flux;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Tools.GitOps;

/// <summary>
/// DI registration for the GitOps skill pack (PR-9): controller-neutral
/// <see cref="IGitOpsController"/> implementations for Flux and Argo CD, the
/// K8sGPT MCP client, the remediation dispatcher, the four agent-facing tools,
/// and the fail-loud startup validator. Composition-root opt-in — callers invoke
/// <see cref="AddGitOpsSkillTools"/> from their <c>Add*Dependencies</c> chain so
/// the skill pack is bolted on only when wanted.
/// </summary>
/// <remarks>
/// <para>
/// Registers, by keyed-DI name:
/// <list type="bullet">
///   <item><c>detect_drift</c>         — <see cref="GitOpsDetectDriftTool"/></item>
///   <item><c>cluster_health</c>       — <see cref="GitOpsClusterHealthTool"/></item>
///   <item><c>propose_remediation</c>  — <see cref="GitOpsProposeRemediationTool"/></item>
///   <item><c>k8sgpt_analyze</c>       — <see cref="K8sGptAnalyzeTool"/></item>
/// </list>
/// </para>
/// <para>
/// Both controllers are registered keyed by their controller name —
/// <c>"flux"</c> and <c>"argocd"</c>. The default
/// <see cref="IGitOpsController"/> resolves to whichever controller
/// <c>AppConfig.AI.GitOps.ActiveController</c> names, so tools inject the active
/// controller directly. Resolving the default throws a clear error when no
/// active controller is configured; <see cref="GitOpsStartupValidator"/> turns
/// that into a fail-loud boot error whenever the skill pack is enabled.
/// </para>
/// <para>
/// Registrations are unconditional (the tools are inert until a skill resolves
/// them), mirroring the workspace skill pack and the Magentic surface. The
/// API clients share the egress-gated named <c>HttpClient</c>
/// (<c>EgressPolicyDelegatingHandler.ClientName</c>) registered by the egress
/// layer — no additional <c>HttpClient</c> registration is needed here.
/// </para>
/// </remarks>
public static class GitOpsDependencyInjection
{
    /// <summary>
    /// Registers the GitOps skill pack's controllers, K8sGPT client, remediation
    /// dispatcher, agent-facing tools, and startup validator on the supplied
    /// <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same collection, for fluent chaining.</returns>
    public static IServiceCollection AddGitOpsSkillTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // --- API clients (share the egress-gated named HttpClient) ---
        services.AddSingleton<FluxApiClient>();
        services.AddSingleton<ArgoCdApiClient>();

        // --- Controllers, keyed by controller name (== AppConfig ActiveController) ---
        services.AddKeyedSingleton<IGitOpsController, FluxGitOpsController>("flux");
        services.AddKeyedSingleton<IGitOpsController, ArgoCdGitOpsController>("argocd");

        // --- Default controller resolves the active one from config ---
        services.AddSingleton<IGitOpsController>(static sp =>
        {
            var active = sp.GetRequiredService<IOptionsMonitor<AppConfig>>().CurrentValue.AI.GitOps.ActiveController;
            if (string.IsNullOrWhiteSpace(active))
            {
                throw new InvalidOperationException(
                    "GitOps is being used but AppConfig.AI.GitOps.ActiveController is not configured. " +
                    "Set it to \"flux\" or \"argocd\".");
            }

            return sp.GetRequiredKeyedService<IGitOpsController>(active);
        });

        // --- K8sGPT client + remediation dispatcher ---
        services.AddSingleton<IK8sGptMcpClient, K8sGptMcpClient>();
        services.AddSingleton<IGitOpsRemediationDispatcher, GitOpsRemediationDispatcher>();

        // --- Agent-facing tools (keyed by ToolName) ---
        services.AddKeyedSingleton<ITool>(GitOpsDetectDriftTool.ToolName, static (sp, _) =>
            new GitOpsDetectDriftTool(sp.GetRequiredService<IGitOpsController>()));

        services.AddKeyedSingleton<ITool>(GitOpsClusterHealthTool.ToolName, static (sp, _) =>
            new GitOpsClusterHealthTool(sp.GetRequiredService<IGitOpsController>()));

        services.AddKeyedSingleton<ITool>(GitOpsProposeRemediationTool.ToolName, static (sp, _) =>
            new GitOpsProposeRemediationTool(
                sp.GetRequiredService<IGitOpsController>(),
                sp.GetRequiredService<IGitOpsRemediationDispatcher>()));

        services.AddKeyedSingleton<ITool>(K8sGptAnalyzeTool.ToolName, static (sp, _) =>
            new K8sGptAnalyzeTool(sp.GetRequiredService<IK8sGptMcpClient>()));

        // --- Fail-loud startup validation when the skill pack is enabled ---
        services.AddHostedService<GitOpsStartupValidator>();

        return services;
    }
}
