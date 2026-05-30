using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Metrics;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Metrics;

public sealed class IsValidJsonMetricTests
{
    private readonly IsValidJsonMetric _sut = new();

    [Fact]
    public void Key_IsIsValidJson() => _sut.Key.Should().Be("is_valid_json");

    [Fact]
    public async Task ScoreAsync_ValidJsonObject_ReturnsPass()
    {
        var score = await _sut.ScoreAsync(Case(), Output("{\"name\":\"x\",\"value\":42}"), Spec(), CancellationToken.None);
        score.Score.Should().Be(1.0);
        score.Verdict.Should().Be(Verdict.Pass);
    }

    [Fact]
    public async Task ScoreAsync_ValidJsonArray_ReturnsPass()
    {
        var score = await _sut.ScoreAsync(Case(), Output("[1, 2, 3]"), Spec(), CancellationToken.None);
        score.Verdict.Should().Be(Verdict.Pass);
    }

    [Fact]
    public async Task ScoreAsync_InvalidJson_ReturnsFail()
    {
        var score = await _sut.ScoreAsync(Case(), Output("{not json"), Spec(), CancellationToken.None);
        score.Score.Should().Be(0.0);
        score.Verdict.Should().Be(Verdict.Fail);
    }

    [Fact]
    public async Task ScoreAsync_EmptyOutput_ReturnsFail()
    {
        var score = await _sut.ScoreAsync(Case(), Output(""), Spec(), CancellationToken.None);
        score.Verdict.Should().Be(Verdict.Fail);
    }

    private static EvalCase Case() => new() { Id = "c1", Input = "in", MetricSpecs = [] };
    private static AgentInvocationResult Output(string text) => new() { Success = true, Output = text };
    private static MetricSpec Spec() => new() { MetricKey = "is_valid_json" };
}
