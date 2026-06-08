using Application.AI.Common.Interfaces.Sandbox;
using Domain.AI.Sandbox;

namespace Infrastructure.AI.Iac;

/// <summary>
/// Shared dispatch helper for the IaC generators. Builds a
/// <see cref="SandboxExecutionRequest"/> for an infrastructure-as-code CLI
/// (terraform / bicep / checkov / tfsec / arm-ttk), runs it through the supplied
/// <see cref="ISandboxExecutor"/>, and returns the raw
/// <see cref="SandboxExecutionResult"/> for the caller to parse.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>WorkspaceCommandRunner</c>: the program and arguments are passed as
/// discrete <c>ArgumentList</c> entries — never a single shell string — so a CLI
/// argument can never smuggle a shell metacharacter through the sandbox boundary.
/// </para>
/// <para>
/// The permission profile grants <see cref="ToolCapability.FileRead"/>,
/// <see cref="ToolCapability.FileWrite"/> (terraform writes a <c>.terraform</c>
/// cache and a plan file; bicep emits an ARM template), <see cref="ToolCapability.Subprocess"/>
/// (the CLIs spawn provider plugins / language servers), and
/// <see cref="ToolCapability.NetworkAccess"/> scoped to the provider/module
/// registries via <see cref="ToolPermissionProfile.AllowedHosts"/>. The egress is
/// the registry allowlist from <c>AppConfig.AI.Iac.RegistryAllowlist</c>; the
/// filesystem scope is the single module directory.
/// </para>
/// </remarks>
public static class IacSandboxRunner
{
    /// <summary>
    /// Runs an IaC CLI inside the sandbox rooted at <paramref name="moduleDirectory"/>.
    /// </summary>
    /// <param name="program">The CLI program to launch (e.g. <c>terraform</c>, <c>bicep</c>, <c>checkov</c>).</param>
    /// <param name="arguments">The discrete CLI arguments — each entry is passed verbatim, never shell-interpreted.</param>
    /// <param name="moduleDirectory">The sandbox-rooted directory the CLI runs against; the sole allowed filesystem path.</param>
    /// <param name="registryAllowlist">The provider/module-registry hosts the run may reach. Seeds the sandbox egress allowlist.</param>
    /// <param name="executor">The sandbox executor to dispatch through.</param>
    /// <param name="toolName">Tool name for diagnostic attribution in the sandbox request.</param>
    /// <param name="timeout">Optional wall-clock timeout. Defaults to 5 minutes — terraform init/plan can be slow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw <see cref="SandboxExecutionResult"/> for the caller to parse.</returns>
    public static async Task<SandboxExecutionResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        string moduleDirectory,
        IReadOnlyList<string> registryAllowlist,
        ISandboxExecutor executor,
        string toolName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleDirectory);
        ArgumentNullException.ThrowIfNull(registryAllowlist);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var profile = new ToolPermissionProfile
        {
            RequiredCapabilities =
                ToolCapability.FileRead
                | ToolCapability.FileWrite
                | ToolCapability.Subprocess
                | ToolCapability.NetworkAccess,
            AllowedPaths = [moduleDirectory],
            AllowedPrograms = [program],
            AllowedHosts = registryAllowlist,
            DeniedHosts = [],
            DeniedPaths = [],
            MinimumIsolation = SandboxIsolationLevel.Process
        };

        var request = new SandboxExecutionRequest
        {
            ToolName = toolName,
            Input = string.Empty,
            Command = program,
            ArgumentList = arguments,
            Limits = new ResourceLimits(),
            PermissionProfile = profile,
            Timeout = timeout ?? TimeSpan.FromMinutes(5)
        };

        return await executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
