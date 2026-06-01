using FluentValidation;

namespace Application.AI.Common.CQRS.Evaluation.IngestEvalRun;

/// <summary>
/// Validates <see cref="IngestEvalRunCommand"/> before execution. Catches
/// null/empty payload + abusive sizes before any store dispatch.
/// </summary>
public sealed class IngestEvalRunCommandValidator : AbstractValidator<IngestEvalRunCommand>
{
    /// <summary>
    /// Maximum number of <c>EvalResult</c> rows in a single ingested report.
    /// One row -> N tracked entities in EF (one case row + M metric-score rows),
    /// so the cap protects against authed DoS via runaway SaveChangesAsync.
    /// Set to 10k — well above any realistic eval suite, well below SQLite
    /// parameter-limit failure modes downstream.
    /// </summary>
    public const int MaxResults = 10_000;

    /// <summary>
    /// Maximum number of <c>EvalDataset</c> entries in a single report. Datasets
    /// are JSON-serialised onto the run header row; a giant dataset list bloats
    /// every list-view query.
    /// </summary>
    public const int MaxDatasets = 256;

    /// <summary>Maximum length of the RunId natural key (matches DB column cap).</summary>
    public const int MaxRunIdLength = 128;

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
                .NotEmpty().WithMessage("Report.RunId is required.")
                .MaximumLength(MaxRunIdLength)
                .WithMessage($"Report.RunId must be at most {MaxRunIdLength} characters.");

            RuleFor(x => x.Report.Results)
                .NotNull().WithMessage("Report.Results is required (empty list is allowed).")
                .Must(r => r is null || r.Count <= MaxResults)
                .WithMessage($"Report.Results must contain at most {MaxResults} entries.");

            RuleFor(x => x.Report.Datasets)
                .NotNull().WithMessage("Report.Datasets is required (empty list is allowed).")
                .Must(d => d is null || d.Count <= MaxDatasets)
                .WithMessage($"Report.Datasets must contain at most {MaxDatasets} entries.");

            RuleFor(x => x.Report.Repeats)
                .GreaterThan(0).WithMessage("Report.Repeats must be positive.");
        });
    }
}
