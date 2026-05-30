using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Metrics;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Metrics;

public sealed class ContainsAllMetricTests
{
    private readonly ContainsAllMetric _sut = new();

    [Fact]
    public void Key_IsContainsAll() => _sut.Key.Should().Be("contains_all");

    [Fact]
    public async Task ScoreAsync_AllValuesPresent_ReturnsPass()
    {
        var spec = Spec("apple|banana|cherry");
        var score = await _sut.ScoreAsync(Case(), Output("I like apple and banana with cherry"), spec, CancellationToken.None);

        score.Score.Should().Be(1.0);
        score.Verdict.Should().Be(Verdict.Pass);
    }

    [Fact]
    public async Task ScoreAsync_SomeValuesMissing_ReturnsPartialScoreAndFail()
    {
        var spec = Spec("apple|banana|cherry", threshold: 1.0);
        var score = await _sut.ScoreAsync(Case(), Output("only apple here"), spec, CancellationToken.None);

        score.Score.Should().BeApproximately(1.0 / 3, 0.001);
        score.Verdict.Should().Be(Verdict.Fail);
    }

    [Fact]
    public async Task ScoreAsync_MissingValuesParam_ReturnsWarn()
    {
        var score = await _sut.ScoreAsync(Case(), Output("any"), Spec(null), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("values");
    }

    private static EvalCase Case() => new() { Id = "c1", Input = "in", MetricSpecs = [] };
    private static AgentInvocationResult Output(string text) => new() { Success = true, Output = text };

    private static MetricSpec Spec(string? values, double threshold = 1.0)
    {
        var parameters = new Dictionary<string, string>();
        if (values is not null) parameters["values"] = values;
        return new MetricSpec { MetricKey = "contains_all", Threshold = threshold, Parameters = parameters };
    }
}
