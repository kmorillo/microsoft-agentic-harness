using Domain.Common.Config;
using Domain.Common.Config.AI.GitOps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.GitOps;

/// <summary>
/// One-shot startup validator for the GitOps skill pack. Runs via
/// <see cref="IHostedService.StartAsync"/> and refuses to boot the host when the
/// skill pack is enabled but its configuration is impossible.
/// </summary>
/// <remarks>
/// <para>
/// Checks performed (only when <c>AppConfig.AI.GitOps.Enabled</c> is true):
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Active controller</b> — <see cref="GitOpsConfig.ActiveController"/>
///     must be <c>"flux"</c> or <c>"argocd"</c>. An empty or unknown value
///     means the default <c>IGitOpsController</c> cannot resolve, so the skill
///     pack would deadlock at the first tool call.
///   </description></item>
///   <item><description>
///     <b>K8sGPT MCP server</b> — <see cref="GitOpsConfig.K8sGptMcpServerName"/>
///     must be a key under <c>AppConfig.AI.McpServers.Servers</c>. K8sGPT is a
///     required dependency of the <c>gitops-cluster-debug</c> skill; the plan
///     rejects graceful degradation that would ship a half-working skill.
///   </description></item>
///   <item><description>
///     <b>Active controller base URL</b> — the Flux or Argo CD base URL for the
///     active controller must be a non-empty, absolute http(s) URL.
///   </description></item>
///   <item><description>
///     <b>Remediation target</b> — <see cref="GitOpsConfig.RemediationRepoUrl"/>
///     must be a non-empty, absolute URL; every remediation plan targets it.
///   </description></item>
/// </list>
/// <para>
/// When the skill pack is disabled the validator no-ops — the tools are inert
/// anyway and consumers exploring the template should not be blocked from
/// running the host.
/// </para>
/// </remarks>
public sealed class GitOpsStartupValidator : IHostedService
{
    private static readonly IReadOnlyList<string> KnownControllers = ["flux", "argocd"];

    private readonly IOptionsMonitor<AppConfig> _config;
    private readonly ILogger<GitOpsStartupValidator> _logger;

    /// <summary>Initializes a new <see cref="GitOpsStartupValidator"/>.</summary>
    /// <param name="config">Application configuration monitor.</param>
    /// <param name="logger">Structured logger.</param>
    public GitOpsStartupValidator(IOptionsMonitor<AppConfig> config, ILogger<GitOpsStartupValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var appConfig = _config.CurrentValue;
        var gitOps = appConfig.AI.GitOps;
        if (!gitOps.Enabled)
        {
            return Task.CompletedTask;
        }

        var controller = ValidateActiveController(gitOps);
        ValidateK8sGptServer(gitOps, appConfig);
        ValidateActiveControllerUrl(gitOps, controller);
        ValidateRemediationTarget(gitOps);

        _logger.LogInformation(
            "GitOps skill pack enabled. ActiveController={Controller}, K8sGptMcpServer={Server}, RemediationRepo={Repo}@{Branch}.",
            controller,
            gitOps.K8sGptMcpServerName,
            gitOps.RemediationRepoUrl,
            gitOps.RemediationBranch);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string ValidateActiveController(GitOpsConfig gitOps)
    {
        var controller = gitOps.ActiveController?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!KnownControllers.Contains(controller))
        {
            throw new InvalidOperationException(
                $"GitOps skill pack is Enabled but AppConfig.AI.GitOps.ActiveController is '{gitOps.ActiveController}'. " +
                "It must be one of: \"flux\", \"argocd\".");
        }

        return controller;
    }

    private static void ValidateK8sGptServer(GitOpsConfig gitOps, AppConfig appConfig)
    {
        if (string.IsNullOrWhiteSpace(gitOps.K8sGptMcpServerName) ||
            !appConfig.AI.McpServers.Servers.ContainsKey(gitOps.K8sGptMcpServerName))
        {
            throw new InvalidOperationException(
                $"GitOps skill pack is Enabled but its K8sGPT MCP server '{gitOps.K8sGptMcpServerName}' " +
                "is not registered under AppConfig.AI.McpServers.Servers. K8sGPT is a required dependency " +
                "of the gitops-cluster-debug skill — add the server entry or disable the skill pack.");
        }
    }

    private static void ValidateActiveControllerUrl(GitOpsConfig gitOps, string controller)
    {
        var (url, propertyName) = controller == "flux"
            ? (gitOps.FluxApiBaseUrl, nameof(GitOpsConfig.FluxApiBaseUrl))
            : (gitOps.ArgoCdApiBaseUrl, nameof(GitOpsConfig.ArgoCdApiBaseUrl));

        if (!IsValidHttpUrl(url))
        {
            throw new InvalidOperationException(
                $"GitOps skill pack is Enabled with ActiveController '{controller}' but " +
                $"AppConfig.AI.GitOps.{propertyName} is not a valid absolute http(s) URL: '{url}'.");
        }
    }

    private static void ValidateRemediationTarget(GitOpsConfig gitOps)
    {
        if (string.IsNullOrWhiteSpace(gitOps.RemediationRepoUrl) ||
            !Uri.TryCreate(gitOps.RemediationRepoUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "GitOps skill pack is Enabled but AppConfig.AI.GitOps.RemediationRepoUrl is empty or not an " +
                $"absolute URL: '{gitOps.RemediationRepoUrl}'. Every remediation plan targets this repository.");
        }
    }

    private static bool IsValidHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
