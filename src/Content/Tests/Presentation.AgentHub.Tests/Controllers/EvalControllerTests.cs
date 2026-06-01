using Application.AI.Common.CQRS.Evaluation.GetEvalRunDetail;
using Application.AI.Common.CQRS.Evaluation.GetEvalRunHistory;
using Application.AI.Common.CQRS.Evaluation.GetPromptVersionComparison;
using Application.AI.Common.CQRS.Evaluation.GetTopRegressedCases;
using Application.AI.Common.CQRS.Evaluation.IngestEvalRun;
using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using Domain.Common;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using Presentation.AgentHub.Controllers;
using Xunit;

namespace Presentation.AgentHub.Tests.Controllers;

/// <summary>
/// Direct controller unit tests — no WebApplicationFactory. Verifies the
/// <see cref="EvalController"/>'s <c>Result&lt;T&gt;</c> → MVC status-code mapping
/// and that each endpoint dispatches the correct command/query through MediatR.
/// </summary>
/// <remarks>
/// Wire-level (auth, routing, middleware) coverage lives in the existing
/// AgentHub WebApplicationFactory infrastructure. Direct controller tests give
/// us focused regression safety on the controller's own logic without paying
/// host-boot cost.
/// </remarks>
public sealed class EvalControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly EvalController _sut;

    public EvalControllerTests()
    {
        _sut = new EvalController(_mediator.Object);
    }

    private static EvalRunReport NewReport(string runId) => new()
    {
        RunId = runId,
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        Datasets = [],
        Results = [],
        OverallVerdict = Verdict.Pass,
        Repeats = 1,
    };

    // --- Ingest ---

    [Fact]
    public async Task Ingest_returns_Ok_on_success()
    {
        var ingested = new IngestEvalRunResult
        {
            RunId = "r1",
            Inserted = true,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
        };
        _mediator.Setup(m => m.Send(It.IsAny<IngestEvalRunCommand>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result<IngestEvalRunResult>.Success(ingested));

        var result = await _sut.Ingest(
            new IngestEvalRunCommand { Report = NewReport("r1") },
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(ingested);
    }

    [Fact]
    public async Task Ingest_returns_500_on_general_failure()
    {
        _mediator.Setup(m => m.Send(It.IsAny<IngestEvalRunCommand>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result<IngestEvalRunResult>.Fail("disk full"));

        var result = await _sut.Ingest(
            new IngestEvalRunCommand { Report = NewReport("r1") },
            CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task Ingest_500_response_does_NOT_leak_internal_error_messages()
    {
        // Security guard: handler-side ex.Message can contain SQL fragments / file paths
        // / schema info on EF/SQLite failures. The controller must NOT pass that through
        // to clients. Per the harness security rules: "Error responses: never leak stack
        // traces, internal paths, or sensitive data."
        const string sensitive = "SQLite Error 14: 'unable to open database file': /var/data/secrets.db";
        _mediator.Setup(m => m.Send(It.IsAny<IngestEvalRunCommand>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result<IngestEvalRunResult>.Fail(sensitive));

        var result = await _sut.Ingest(
            new IngestEvalRunCommand { Report = NewReport("r1") },
            CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        var details = problem.Value as Microsoft.AspNetCore.Mvc.ProblemDetails;
        details.Should().NotBeNull();
        details!.Detail.Should().NotContain("SQLite", "raw exception messages must not leak through ToActionResult on General failures");
        details.Detail.Should().NotContain("secrets.db", "file paths must not leak through ToActionResult on General failures");
    }

    [Fact]
    public async Task Ingest_returns_400_on_validation_failure()
    {
        _mediator.Setup(m => m.Send(It.IsAny<IngestEvalRunCommand>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result<IngestEvalRunResult>.ValidationFailure(["Report.RunId is required."]));

        var result = await _sut.Ingest(
            new IngestEvalRunCommand { Report = NewReport("") },
            CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // --- History ---

    [Fact]
    public async Task GetHistory_dispatches_query_with_Take_param()
    {
        GetEvalRunHistoryQuery? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<GetEvalRunHistoryQuery>(), It.IsAny<CancellationToken>()))
                 .Callback<IRequest<Result<IReadOnlyList<EvalRunSummary>>>, CancellationToken>(
                     (q, _) => captured = (GetEvalRunHistoryQuery)q)
                 .ReturnsAsync(Result<IReadOnlyList<EvalRunSummary>>.Success([]));

        await _sut.GetHistory(take: 7, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Take.Should().Be(7);
    }

    // --- Detail ---

    [Fact]
    public async Task GetDetail_returns_404_when_NotFound()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetEvalRunDetailQuery>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result<EvalRunReport>.NotFound("unknown"));

        var result = await _sut.GetDetail("r-missing", CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetDetail_returns_Ok_when_present()
    {
        var report = NewReport("r1");
        _mediator.Setup(m => m.Send(It.IsAny<GetEvalRunDetailQuery>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result<EvalRunReport>.Success(report));

        var result = await _sut.GetDetail("r1", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(report);
    }

    // --- Prompt-version compare ---

    [Fact]
    public async Task ComparePromptVersions_dispatches_with_promptName()
    {
        GetPromptVersionComparisonQuery? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<GetPromptVersionComparisonQuery>(), It.IsAny<CancellationToken>()))
                 .Callback<IRequest<Result<IReadOnlyList<PromptVersionComparisonRow>>>, CancellationToken>(
                     (q, _) => captured = (GetPromptVersionComparisonQuery)q)
                 .ReturnsAsync(Result<IReadOnlyList<PromptVersionComparisonRow>>.Success([]));

        await _sut.ComparePromptVersions("faithfulness-judge", CancellationToken.None);

        captured!.PromptName.Should().Be("faithfulness-judge");
    }

    // --- Regressions ---

    [Fact]
    public async Task GetRegressions_dispatches_with_current_baseline_and_take()
    {
        GetTopRegressedCasesQuery? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<GetTopRegressedCasesQuery>(), It.IsAny<CancellationToken>()))
                 .Callback<IRequest<Result<IReadOnlyList<RegressedCaseRow>>>, CancellationToken>(
                     (q, _) => captured = (GetTopRegressedCasesQuery)q)
                 .ReturnsAsync(Result<IReadOnlyList<RegressedCaseRow>>.Success([]));

        await _sut.GetRegressions("current", "baseline", 5, CancellationToken.None);

        captured!.CurrentRunId.Should().Be("current");
        captured.BaselineRunId.Should().Be("baseline");
        captured.Take.Should().Be(5);
    }

    [Fact]
    public async Task GetRegressions_returns_404_when_run_missing()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetTopRegressedCasesQuery>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result<IReadOnlyList<RegressedCaseRow>>.NotFound("missing"));

        var result = await _sut.GetRegressions("current", "baseline", 5, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void Constructor_rejects_null_mediator()
    {
        var act = () => new EvalController(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
