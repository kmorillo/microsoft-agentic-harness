using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.Core.CQRS.Evaluation.RunEvalSuite;
using Domain.AI.Evaluation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.CQRS;

public sealed class RunEvalSuiteCommandHandlerTests : IDisposable
{
    private readonly string _tempDir;

    public RunEvalSuiteCommandHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "eval-handler-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string CreateFile(string name, string content = "stub")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static EvalDataset MakeDataset(string name = "d") => new()
    {
        Name = name,
        Cases = [new EvalCase
        {
            Id = "c1",
            Input = "i",
            MetricSpecs = [new MetricSpec { MetricKey = "exact_match" }]
        }]
    };

    private static EvalRunReport MakeReport(string runId = "run-1") => new()
    {
        RunId = runId,
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Duration = TimeSpan.FromMilliseconds(1),
        Datasets = [MakeDataset()],
        Results = [],
        OverallVerdict = Verdict.Pass
    };

    private static Mock<IEvalDatasetLoader> Loader(string ext, Func<string, EvalDataset>? loadFunc = null)
        => Loader(new[] { ext }, loadFunc);

    private static Mock<IEvalDatasetLoader> Loader(IReadOnlyList<string> exts, Func<string, EvalDataset>? loadFunc = null)
    {
        var mock = new Mock<IEvalDatasetLoader>();
        mock.SetupGet(l => l.Extensions).Returns(exts);
        mock.Setup(l => l.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string p, CancellationToken _) =>
                loadFunc is not null ? loadFunc(p) : MakeDataset(Path.GetFileNameWithoutExtension(p)));
        return mock;
    }

    private static RunEvalSuiteCommandHandler MakeSut(
        IEnumerable<IEvalDatasetLoader> loaders,
        IEvalRunner runner) => new(
            loaders,
            runner,
            NullLogger<RunEvalSuiteCommandHandler>.Instance);

    [Fact]
    public async Task Loads_each_dataset_via_extension_match_and_passes_to_runner()
    {
        var path1 = CreateFile("a.yaml");
        var path2 = CreateFile("b.yaml");

        IReadOnlyList<EvalDataset>? capturedDatasets = null;
        var loader = Loader("yaml");
        var runner = new Mock<IEvalRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<IReadOnlyList<EvalDataset>>(), It.IsAny<EvalRunOptions>(), It.IsAny<CancellationToken>()))
              .Callback<IReadOnlyList<EvalDataset>, EvalRunOptions, CancellationToken>((d, _, _) => capturedDatasets = d)
              .ReturnsAsync(MakeReport());

        var sut = MakeSut([loader.Object], runner.Object);

        var result = await sut.Handle(new RunEvalSuiteCommand
        {
            DatasetPaths = [path1, path2],
            Options = new EvalRunOptions()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedDatasets.Should().NotBeNull().And.HaveCount(2);
        loader.Verify(l => l.LoadAsync(path1, It.IsAny<CancellationToken>()), Times.Once);
        loader.Verify(l => l.LoadAsync(path2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Extension_matching_is_case_insensitive_and_handles_leading_dot()
    {
        var path = CreateFile("a.YAML");
        var loader = Loader("yaml");
        var runner = new Mock<IEvalRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<IReadOnlyList<EvalDataset>>(), It.IsAny<EvalRunOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeReport());

        var sut = MakeSut([loader.Object], runner.Object);

        var result = await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [path] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Loader_registered_for_multiple_extensions_handles_each_spelling()
    {
        var ymlPath = CreateFile("a.yml");
        var yamlPath = CreateFile("b.yaml");

        var loader = Loader(new[] { "yaml", "yml" });
        var runner = new Mock<IEvalRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<IReadOnlyList<EvalDataset>>(), It.IsAny<EvalRunOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeReport());

        var sut = MakeSut([loader.Object], runner.Object);

        var result = await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [ymlPath, yamlPath] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        loader.Verify(l => l.LoadAsync(ymlPath, It.IsAny<CancellationToken>()), Times.Once);
        loader.Verify(l => l.LoadAsync(yamlPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Returns_validation_failure_when_dataset_paths_empty()
    {
        var loader = Loader("yaml");
        var runner = new Mock<IEvalRunner>(MockBehavior.Strict);

        var sut = MakeSut([loader.Object], runner.Object);

        var result = await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(Domain.Common.ResultFailureType.Validation);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_not_found_when_dataset_file_missing()
    {
        var missing = Path.Combine(_tempDir, "missing.yaml");
        var runner = new Mock<IEvalRunner>(MockBehavior.Strict);
        var loader = Loader("yaml");

        var sut = MakeSut([loader.Object], runner.Object);

        var result = await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [missing] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(Domain.Common.ResultFailureType.NotFound);
        result.Errors.Should().Contain(e => e.Contains("missing.yaml"));
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_failure_when_no_loader_registered_for_extension()
    {
        var path = CreateFile("a.toml");
        var loader = Loader("yaml");
        var runner = new Mock<IEvalRunner>(MockBehavior.Strict);

        var sut = MakeSut([loader.Object], runner.Object);

        var result = await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [path] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("toml"));
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Returns_failure_when_loader_throws_invalid_data()
    {
        var path = CreateFile("a.yaml");
        var loader = new Mock<IEvalDatasetLoader>();
        loader.SetupGet(l => l.Extensions).Returns(new[] { "yaml" });
        loader.Setup(l => l.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidDataException("bad yaml"));

        var runner = new Mock<IEvalRunner>(MockBehavior.Strict);
        var sut = MakeSut([loader.Object], runner.Object);

        var result = await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [path] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("bad yaml"));
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Forwards_options_to_runner()
    {
        var path = CreateFile("a.yaml");
        var loader = Loader("yaml");

        EvalRunOptions? capturedOptions = null;
        var runner = new Mock<IEvalRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<IReadOnlyList<EvalDataset>>(), It.IsAny<EvalRunOptions>(), It.IsAny<CancellationToken>()))
              .Callback<IReadOnlyList<EvalDataset>, EvalRunOptions, CancellationToken>((_, o, _) => capturedOptions = o)
              .ReturnsAsync(MakeReport());

        var options = new EvalRunOptions { Repeats = 3, Parallelism = 4, FailRateThreshold = 0.1, ForceDeterministic = true };

        var sut = MakeSut([loader.Object], runner.Object);
        await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [path], Options = options }, CancellationToken.None);

        capturedOptions.Should().BeSameAs(options);
    }

    [Fact]
    public async Task Returns_report_from_runner_on_success()
    {
        var path = CreateFile("a.yaml");
        var loader = Loader("yaml");
        var report = MakeReport("expected-run-id");

        var runner = new Mock<IEvalRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<IReadOnlyList<EvalDataset>>(), It.IsAny<EvalRunOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(report);

        var sut = MakeSut([loader.Object], runner.Object);
        var result = await sut.Handle(new RunEvalSuiteCommand { DatasetPaths = [path] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(report);
    }
}
