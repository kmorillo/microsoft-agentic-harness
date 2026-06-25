using Domain.Common.Config.AI.Governance;
using FluentValidation;

namespace Application.Core.Validation;

/// <summary>
/// Validates <see cref="DataClassificationConfig"/>. All rules are conditional on classification being
/// enabled (<see cref="DataClassificationConfig.Mode"/> not <see cref="ClassificationEnforcementMode.Off"/>) —
/// the gate is off by default and a disabled section imposes no constraints, so the template runs out of
/// the box. When enabled the rules ensure the label→action map is coherent: a blank label key can never
/// match a real Purview label and signals a config typo that would silently never fire.
/// </summary>
/// <remarks>
/// Auto-discovered via <c>AddValidatorsFromAssembly</c> on the Application.Core assembly — no manual
/// registration required.
/// </remarks>
public sealed class DataClassificationConfigValidator : AbstractValidator<DataClassificationConfig>
{
    /// <summary>Initializes a new instance of the <see cref="DataClassificationConfigValidator"/> class.</summary>
    public DataClassificationConfigValidator()
    {
        When(x => x.Mode != ClassificationEnforcementMode.Off, () =>
        {
            RuleFor(x => x.LabelActions)
                .NotNull()
                .WithMessage("LabelActions must not be null when classification is enabled.");

            RuleForEach(x => x.LabelActions)
                .Must(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                .WithMessage(
                    "LabelActions contains a blank label name. A blank key can never match a Purview " +
                    "sensitivity label, so the rule would never fire — remove it or supply the label name.");
        });
    }
}
