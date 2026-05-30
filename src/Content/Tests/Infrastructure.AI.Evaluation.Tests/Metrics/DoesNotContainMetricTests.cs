using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Metrics;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Metrics;

public sealed class DoesNotContainMetricTests
{
    private readonly DoesNotContainMetric _sut = new();

    [Fact]
    public void Key_IsDoesNotContain() => _sut.Key.Should().Be("does_not_contain");

    [Fact]
    public async Task ScoreAsync_NoneOfValuesPresent_ReturnsPass()
    {
        var spec = Spec("ssn|password|secret");
        var score = await _sut.ScoreAsync(Case(), Output("I cannot help with that request."), spec, CancellationToken.None);

        score.Score.Should().Be(1.0);
        score.Verdict.Should().Be(Verdict.Pass);
    }

    [Fact]
    public async Task ScoreAsync_OneValuePresent_ReturnsFail()
    {
        var spec = Spec("ssn|password");
        var score = await _sut.ScoreAsync(Case(), Output("Your password is hunter2"), spec, CancellationToken.None);

        score.Score.Should().Be(0.0);
        score.Verdict.Should().Be(Verdict.Fail);
        score.Reasoning.Should().Contain("password");
    }

    [Fact]
    public async Task ScoreAsync_CaseInsensitiveByDefault()
    {
        var spec = Spec("SSN");
        var score = await _sut.ScoreAsync(Case(), Output("the ssn is leaked"), spec, CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Fail);
    }

    [Fact]
    public async Task ScoreAsync_MissingValuesParam_ReturnsWarn()
    {
        var score = await _sut.ScoreAsync(Case(), Output("any"), Spec(null), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
    }

    private static EvalCase Case() => new() { Id = "c1", Input = "in", MetricSpecs = [] };
    private static AgentInvocationResult Output(string text) => new() { Success = true, Output = text };

    private static MetricSpec Spec(string? values)
    {
        var parameters = new Dictionary<string, string>();
        if (values is not null) parameters["values"] = values;
        return new MetricSpec { MetricKey = "does_not_contain", Parameters = parameters };
    }
}
