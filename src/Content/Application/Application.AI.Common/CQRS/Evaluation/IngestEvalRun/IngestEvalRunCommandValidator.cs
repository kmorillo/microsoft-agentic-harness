using FluentValidation;

namespace Application.AI.Common.CQRS.Evaluation.IngestEvalRun;

/// <summary>
/// Validates <see cref="IngestEvalRunCommand"/> before execution. Catches
/// null/empty payload before any store dispatch.
/// </summary>
public sealed class IngestEvalRunCommandValidator : AbstractValidator<IngestEvalRunCommand>
{
    /// <summary>Initializes the validator with rules for command shape.</summary>
    public IngestEvalRunCommandValidator()
    {
        RuleFor(x => x.Report)
            .NotNull().WithMessage("Report is required.");

        // Skip child-property rules when Report itself is null — FluentValidation
        // would otherwise NRE on the property accessors.
        When(x => x.Report is not null, () =>
        {
            RuleFor(x => x.Report.RunId)
                .NotEmpty().WithMessage("Report.RunId is required.");

            RuleFor(x => x.Report.Results)
                .NotNull().WithMessage("Report.Results is required (empty list is allowed).");

            RuleFor(x => x.Report.Datasets)
                .NotNull().WithMessage("Report.Datasets is required (empty list is allowed).");

            RuleFor(x => x.Report.Repeats)
                .GreaterThan(0).WithMessage("Report.Repeats must be positive.");
        });
    }
}
