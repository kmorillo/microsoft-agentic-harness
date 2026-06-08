using Domain.AI.Iac;
using Domain.Common.Config;
using Domain.Common.Config.AI.Iac;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Iac;

/// <summary>
/// One-shot startup validator for the IaC skill pack (PR-10). Runs via
/// <see cref="IHostedService.StartAsync"/> and refuses to boot the host when the
/// skill pack is enabled but its configuration is impossible.
/// </summary>
/// <remarks>
/// <para>
/// Checks performed (only when <c>AppConfig.AI.Iac.Enabled</c> is true):
/// </para>
/// <list type="number">
///   <item><description><b>Enabled backends</b> — non-empty, and every entry is a known backend (<c>terraform</c> / <c>bicep</c>).</description></item>
///   <item><description><b>Version pins</b> — each enabled backend (and its scanners) carries a non-empty pinned version.</description></item>
///   <item><description><b>Blocking severity</b> — parses to a defined <see cref="IacScanSeverity"/>.</description></item>
///   <item><description><b>Registry allowlist</b> — non-empty; the sandbox egress allowlist for plan/scan must seed from something.</description></item>
/// </list>
/// <para>
/// When the skill pack is disabled the validator no-ops — the tools are inert and a
/// consumer exploring the template should not be blocked from running the host.
/// </para>
/// </remarks>
public sealed class IacStartupValidator : IHostedService
{
    private readonly IOptionsMonitor<AppConfig> _config;
    private readonly ILogger<IacStartupValidator> _logger;

    /// <summary>Initialises a new <see cref="IacStartupValidator"/>.</summary>
    /// <param name="config">Application configuration monitor.</param>
    /// <param name="logger">Structured logger.</param>
    public IacStartupValidator(IOptionsMonitor<AppConfig> config, ILogger<IacStartupValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var iac = _config.CurrentValue.AI.Iac;
        if (!iac.Enabled)
        {
            return Task.CompletedTask;
        }

        var backends = ValidateEnabledBackends(iac);
        ValidateVersionPins(iac, backends);
        ValidateBlockingSeverity(iac);
        ValidateRegistryAllowlist(iac);

        _logger.LogInformation(
            "IaC skill pack enabled. Backends={Backends}, BlockingSeverity={Severity}, Registries={Registries}.",
            string.Join(",", backends),
            iac.BlockingSeverity,
            iac.RegistryAllowlist.Count);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static IReadOnlyList<IacBackend> ValidateEnabledBackends(IacConfig iac)
    {
        if (iac.EnabledBackends is null || iac.EnabledBackends.Count == 0)
        {
            throw new InvalidOperationException(
                "IaC skill pack is Enabled but AppConfig.AI.Iac.EnabledBackends is empty. " +
                "Enable at least one of: \"terraform\", \"bicep\".");
        }

        var backends = new List<IacBackend>(iac.EnabledBackends.Count);
        foreach (var entry in iac.EnabledBackends)
        {
            if (!IacBackendKeys.TryParse(entry, out var backend))
            {
                throw new InvalidOperationException(
                    $"IaC skill pack is Enabled but AppConfig.AI.Iac.EnabledBackends contains an unknown backend '{entry}'. " +
                    "Known backends: \"terraform\", \"bicep\".");
            }

            backends.Add(backend);
        }

        return backends;
    }

    private static void ValidateVersionPins(IacConfig iac, IReadOnlyList<IacBackend> backends)
    {
        foreach (var backend in backends)
        {
            switch (backend)
            {
                case IacBackend.Terraform:
                    RequirePin(iac.TerraformVersion, nameof(IacConfig.TerraformVersion));
                    RequirePin(iac.CheckovVersion, nameof(IacConfig.CheckovVersion));
                    RequirePin(iac.TfsecVersion, nameof(IacConfig.TfsecVersion));
                    break;
                case IacBackend.Bicep:
                    RequirePin(iac.BicepVersion, nameof(IacConfig.BicepVersion));
                    RequirePin(iac.ArmTtkVersion, nameof(IacConfig.ArmTtkVersion));
                    RequirePin(iac.CheckovVersion, nameof(IacConfig.CheckovVersion));
                    break;
            }
        }
    }

    private static void RequirePin(string version, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                $"IaC skill pack is Enabled but AppConfig.AI.Iac.{propertyName} is empty. " +
                "Every enabled backend's CLI must carry a pinned version baked into the sandbox image.");
        }
    }

    private static void ValidateBlockingSeverity(IacConfig iac)
    {
        if (!IacScanSeverityParser.TryParse(iac.BlockingSeverity, out _))
        {
            throw new InvalidOperationException(
                $"IaC skill pack is Enabled but AppConfig.AI.Iac.BlockingSeverity is '{iac.BlockingSeverity}'. " +
                "It must be one of: Low, Medium, High, Critical.");
        }
    }

    private static void ValidateRegistryAllowlist(IacConfig iac)
    {
        if (iac.RegistryAllowlist is null || iac.RegistryAllowlist.Count == 0)
        {
            throw new InvalidOperationException(
                "IaC skill pack is Enabled but AppConfig.AI.Iac.RegistryAllowlist is empty. " +
                "Plan/scan runs need outbound access to the provider/module registries.");
        }
    }
}
