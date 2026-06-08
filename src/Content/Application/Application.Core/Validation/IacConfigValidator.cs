using Domain.AI.Iac;
using Domain.Common.Config.AI.Iac;
using FluentValidation;

namespace Application.Core.Validation;

/// <summary>
/// Validates <see cref="IacConfig"/>. All rules are conditional on
/// <see cref="IacConfig.Enabled"/> — a disabled skill pack imposes no constraints so
/// the template runs out of the box. When enabled the rules mirror
/// <c>IacStartupValidator</c> so a misconfiguration is caught both by the
/// options-validation pipeline and at host boot.
/// </summary>
public sealed class IacConfigValidator : AbstractValidator<IacConfig>
{
    private static readonly string[] KnownBackends = [IacBackendKeys.Terraform, IacBackendKeys.Bicep];

    /// <summary>Initializes a new instance of the <see cref="IacConfigValidator"/> class.</summary>
    public IacConfigValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.EnabledBackends)
                .NotEmpty()
                .WithMessage("EnabledBackends must contain at least one backend when IaC is enabled.")
                .Must(AllKnown)
                .WithMessage("EnabledBackends may only contain \"terraform\" or \"bicep\" when IaC is enabled.");

            RuleFor(x => x.BlockingSeverity)
                .Must(s => IacScanSeverityParser.TryParse(s, out _))
                .WithMessage("BlockingSeverity must be one of Low, Medium, High, Critical when IaC is enabled.");

            RuleFor(x => x.RegistryAllowlist)
                .NotEmpty()
                .WithMessage("RegistryAllowlist must be non-empty when IaC is enabled (plan/scan need registry egress).");

            When(BackendEnabled(IacBackendKeys.Terraform), () =>
            {
                RuleFor(x => x.TerraformVersion).NotEmpty()
                    .WithMessage("TerraformVersion is required when terraform is enabled.");
                RuleFor(x => x.CheckovVersion).NotEmpty()
                    .WithMessage("CheckovVersion is required when terraform is enabled.");
                RuleFor(x => x.TfsecVersion).NotEmpty()
                    .WithMessage("TfsecVersion is required when terraform is enabled.");
            });

            When(BackendEnabled(IacBackendKeys.Bicep), () =>
            {
                RuleFor(x => x.BicepVersion).NotEmpty()
                    .WithMessage("BicepVersion is required when bicep is enabled.");
                RuleFor(x => x.ArmTtkVersion).NotEmpty()
                    .WithMessage("ArmTtkVersion is required when bicep is enabled.");
                RuleFor(x => x.CheckovVersion).NotEmpty()
                    .WithMessage("CheckovVersion is required when bicep is enabled.");
            });
        });
    }

    private static bool AllKnown(List<string> backends)
        => backends is { Count: > 0 }
           && backends.All(b => KnownBackends.Contains(b?.Trim().ToLowerInvariant()));

    private static Func<IacConfig, bool> BackendEnabled(string key)
        => config => config.EnabledBackends is { Count: > 0 }
                     && config.EnabledBackends.Any(b => string.Equals(b?.Trim(), key, StringComparison.OrdinalIgnoreCase));
}
