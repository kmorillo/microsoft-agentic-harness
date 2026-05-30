using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Metrics;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Metrics;

public sealed class ExactMatchMetricTests
{
    private readonly ExactMatchMetric _sut = new();

    [Fact]
    public void Key_IsExactMatch() => _sut.Key.Should().Be("exact_match");

    [Fact]
    public async Task ScoreAsync_OutputEqualsExpected_ReturnsPass()
    {
        var score = await _sut.ScoreAsync(
            Case("hello"),
            Output("hello"),
            Spec(threshold: 1.0),
            CancellationToken.None);

        score.Score.Should().Be(1.0);
        score.Verdict.Should().Be(Verdict.Pass);
    }

    [Fact]
    public async Task ScoreAsync_OutputDiffers_ReturnsFail()
    {
        var score = await _sut.ScoreAsync(Case("hello"), Output("world"), Spec(), CancellationToken.None);

        score.Score.Should().Be(0.0);
        score.Verdict.Should().Be(Verdict.Fail);
    }

    [Fact]
    public async Task ScoreAsync_CaseInsensitive_MatchesIgnoringCase()
    {
        var spec = Spec(parameters: new Dictionary<string, string> { ["case_sensitive"] = "false" });
        var score = await _sut.ScoreAsync(Case("Hello"), Output("HELLO"), spec, CancellationToken.None);

        score.Score.Should().Be(1.0);
        score.Verdict.Should().Be(Verdict.Pass);
    }

    [Fact]
    public async Task ScoreAsync_NullExpected_ReturnsWarn()
    {
        var score = await _sut.ScoreAsync(Case(null), Output("anything"), Spec(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("expected_output");
    }

    private static EvalCase Case(string? expected) => new()
    {
        Id = "c1",
        Input = "in",
        ExpectedOutput = expected,
        MetricSpecs = []
    };

    private static AgentInvocationResult Output(string text) => new() { Success = true, Output = text };

    private static MetricSpec Spec(double threshold = 1.0, IReadOnlyDictionary<string, string>? parameters = null) => new()
    {
        MetricKey = "exact_match",
        Threshold = threshold,
        Parameters = parameters ?? new Dictionary<string, string>()
    };
}
