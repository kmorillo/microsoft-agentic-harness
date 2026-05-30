using Application.AI.Common.Evaluation.Models;
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Evaluation;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Metrics;

public class LlmJudgeMetricTests
{
    private static EvalCase MakeCase(string input = "in", string? expected = "exp") => new()
    {
        Id = "c1",
        Input = input,
        ExpectedOutput = expected,
        MetricSpecs = [new MetricSpec { MetricKey = "llm_judge" }]
    };

    private static AgentInvocationResult MakeOutput(string text = "out") => new()
    {
        Output = text,
        Success = true,
        Duration = TimeSpan.FromMilliseconds(1)
    };

    private static MetricSpec SpecWithRubric(string rubric = "Score the answer 0-1.", double threshold = 0.7) => new()
    {
        MetricKey = "llm_judge",
        Threshold = threshold,
        Parameters = new Dictionary<string, string> { ["rubric"] = rubric }
    };

    private static ModelTier StubTier() => new()
    {
        Name = "test-judge",
        ClientType = AIAgentFrameworkClientType.AzureOpenAI,
        DeploymentName = "test-deployment"
    };

    private static (Mock<IModelRouter> router, Mock<IChatClient> client) RouterReturning(params string[] responses)
    {
        var clientMock = new Mock<IChatClient>();
        var queue = new Queue<string>(responses);
        clientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var text = queue.Count > 0 ? queue.Dequeue() : "{}";
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
            });

        var routerMock = new Mock<IModelRouter>();
        routerMock.Setup(r => r.RouteOperationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelRoutingDecision
            {
                SelectedTier = StubTier(),
                Client = clientMock.Object,
                Complexity = TaskComplexity.Simple,
                Source = ClassificationSource.Heuristic,
                Confidence = 1.0
            });

        return (routerMock, clientMock);
    }

    private static Mock<IModelRouter> RouterThrowing(Exception ex)
    {
        var router = new Mock<IModelRouter>();
        router.Setup(r => r.RouteOperationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
        return router;
    }

    [Fact]
    public void Key_returns_llm_judge()
    {
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            Mock.Of<IModelRouter>(),
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        sut.Key.Should().Be("llm_judge");
    }

    [Fact]
    public async Task Returns_warn_when_rubric_missing_and_does_not_call_router()
    {
        var router = new Mock<IModelRouter>(MockBehavior.Strict);
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var spec = new MetricSpec { MetricKey = "llm_judge" };

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), spec, CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.MetricKey.Should().Be("llm_judge");
        router.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Parses_clean_json_and_returns_pass_when_score_meets_threshold()
    {
        var (router, _) = RouterReturning("""{"score": 0.85, "reasoning": "Looks great."}""");
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(threshold: 0.7), CancellationToken.None);

        score.Score.Should().Be(0.85);
        score.Verdict.Should().Be(Verdict.Pass);
        score.Reasoning.Should().Be("Looks great.");
        score.RawOutput.Should().Contain("0.85");
    }

    [Fact]
    public async Task Returns_fail_when_score_below_threshold()
    {
        var (router, _) = RouterReturning("""{"score": 0.4, "reasoning": "Off topic."}""");
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(threshold: 0.7), CancellationToken.None);

        score.Score.Should().Be(0.4);
        score.Verdict.Should().Be(Verdict.Fail);
    }

    [Fact]
    public async Task Strips_markdown_fences_before_parsing()
    {
        var fenced = "```json\n{\"score\": 1.0, \"reasoning\": \"ok\"}\n```";
        var (router, _) = RouterReturning(fenced);
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Score.Should().Be(1.0);
    }

    [Fact]
    public async Task Retries_once_on_malformed_json_and_passes_when_retry_succeeds()
    {
        var (router, client) = RouterReturning(
            "not json at all",
            """{"score": 0.9, "reasoning": "Recovered."}""");
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Score.Should().Be(0.9);
        client.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Returns_warn_when_both_attempts_malformed()
    {
        var (router, client) = RouterReturning("garbage 1", "garbage 2");
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Score.Should().Be(0.0);
        score.RawOutput.Should().NotBeNullOrEmpty();
        client.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Clamps_score_into_zero_one_range()
    {
        var (router, _) = RouterReturning("""{"score": 1.5, "reasoning": "over"}""");
        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(threshold: 0.9), CancellationToken.None);

        score.Score.Should().Be(1.0);
        score.Verdict.Should().Be(Verdict.Pass);
    }

    [Fact]
    public async Task Returns_warn_when_router_throws()
    {
        var router = RouterThrowing(new InvalidOperationException("boom"));

        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        var score = await sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("boom");
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        var router = RouterThrowing(new OperationCanceledException());

        var sut = new Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric(
            router.Object,
            NullLogger<Infrastructure.AI.Evaluation.Metrics.LlmJudgeMetric>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.ScoreAsync(MakeCase(), MakeOutput(), SpecWithRubric(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
