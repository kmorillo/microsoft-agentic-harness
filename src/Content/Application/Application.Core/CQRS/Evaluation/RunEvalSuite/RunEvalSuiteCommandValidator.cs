using FluentValidation;

namespace Application.Core.CQRS.Evaluation.RunEvalSuite;

/// <summary>
/// Validates <see cref="RunEvalSuiteCommand"/> before execution. Catches obvious
/// misconfigurations (empty path list, invalid repeats/parallelism) before any work begins.
/// </summary>
public sealed class RunEvalSuiteCommandValidator : AbstractValidator<RunEvalSuiteCommand>
{
    /// <summary>Initializes the validator with rules for command shape.</summary>
    public RunEvalSuiteCommandValidator()
    {
        RuleFor(x => x.DatasetPaths)
            .NotEmpty().WithMessage("At least one dataset path is required.");

        RuleForEach(x => x.DatasetPaths)
            .NotEmpty().WithMessage("Dataset paths must not be empty strings.");

        RuleFor(x => x.Options.Repeats)
            .InclusiveBetween(1, 50)
            .WithMessage("Repeats must be between 1 and 50.");

        RuleFor(x => x.Options.Parallelism)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Parallelism must be at least 1.");

        RuleFor(x => x.Options.FailRateThreshold)
            .InclusiveBetween(0.0, 1.0)
            .WithMessage("FailRateThreshold must be between 0.0 and 1.0.");
    }
}
