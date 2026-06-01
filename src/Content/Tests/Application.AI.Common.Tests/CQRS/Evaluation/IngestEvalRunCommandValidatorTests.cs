using Application.AI.Common.CQRS.Evaluation.IngestEvalRun;
using Domain.AI.Evaluation;
using FluentValidation.TestHelper;
using Xunit;

namespace Application.AI.Common.Tests.CQRS.Evaluation;

public sealed class IngestEvalRunCommandValidatorTests
{
    private readonly IngestEvalRunCommandValidator _sut = new();

    private static EvalRunReport ValidReport() => new()
    {
        RunId = "run-1",
        StartedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
        CompletedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 1, 0, TimeSpan.Zero),
        Duration = TimeSpan.FromMinutes(1),
        Datasets = [],
        Results = [],
        OverallVerdict = Verdict.Pass,
        Repeats = 1,
    };

    private static IngestEvalRunCommand Valid() => new() { Report = ValidReport() };

    [Fact]
    public void Valid_command_has_no_errors()
    {
        var result = _sut.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Null_Report_fails()
    {
        var result = _sut.TestValidate(new IngestEvalRunCommand { Report = null! });
        result.ShouldHaveValidationErrorFor(c => c.Report);
    }

    [Fact]
    public void Missing_RunId_fails()
    {
        var report = ValidReport() with { RunId = "" };
        var result = _sut.TestValidate(new IngestEvalRunCommand { Report = report });
        result.ShouldHaveValidationErrorFor("Report.RunId");
    }

    [Fact]
    public void Zero_Repeats_fails()
    {
        var report = ValidReport() with { Repeats = 0 };
        var result = _sut.TestValidate(new IngestEvalRunCommand { Report = report });
        result.ShouldHaveValidationErrorFor("Report.Repeats");
    }

    [Fact]
    public void Empty_Results_list_is_allowed()
    {
        var report = ValidReport() with { Results = [] };
        var result = _sut.TestValidate(new IngestEvalRunCommand { Report = report });
        result.ShouldNotHaveValidationErrorFor("Report.Results");
    }
}
