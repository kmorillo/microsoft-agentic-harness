using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.AI.Common.Evaluation.Outcomes;
using Application.AI.Common.Prompts.Interfaces;
using Application.AI.Common.Prompts.Models;
using Domain.AI.Evaluation;
using Domain.AI.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.Evaluation.Tests.Metrics.Rag;

/// <summary>
/// Shared fixtures + helpers used by every RAG metric test class.
/// </summary>
internal static class RagMetricTestHarness
{
    public static AgentInvocationResult Output(string text = "agent answer here.") => new()
    {
        Output = text,
        Success = true,
        Duration = TimeSpan.FromMilliseconds(1)
    };

    public static EvalCase Case(
        string id = "c1",
        string input = "What is the capital of France?",
        string? expected = "Paris",
        string? retrieved = "France's capital is Paris.")
        => new()
        {
            Id = id,
            Input = input,
            ExpectedOutput = expected,
            RetrievedContext = retrieved,
            MetricSpecs = [new MetricSpec { MetricKey = "test" }]
        };

    public static MetricSpec Spec(double threshold = 0.7) => new()
    {
        MetricKey = "test",
        Threshold = threshold
    };

    public static (Mock<ILlmJudge> judge, Mock<IPromptRegistry> registry, Mock<IPromptUsageRecorder> recorder) Plumbing(
        LlmJudgeResult judgeResult,
        string templateBody = "Score: {{output}}")
    {
        var judge = new Mock<ILlmJudge>();
        judge.Setup(j => j.JudgeAsync(It.IsAny<LlmJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResult);

        var registry = new Mock<IPromptRegistry>();
        registry.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => new PromptDescriptor
            {
                Name = name,
                Version = new PromptVersion(1, 0),
                ContentHash = "deadbeef",
                Body = templateBody,
            });

        var recorder = new Mock<IPromptUsageRecorder>();
        recorder.Setup(r => r.RecordAsync(
                It.IsAny<PromptDescriptor>(),
                It.IsAny<PromptUsageContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PromptDescriptor d, PromptUsageContext ctx, CancellationToken _) => new PromptUsageRecord
            {
                Descriptor = d,
                CaseId = ctx.CaseId,
                MetricKey = ctx.MetricKey,
                RecordedAtUtc = DateTimeOffset.UtcNow,
            });

        return (judge, registry, recorder);
    }

    public static LlmJudgeResult Parsed(double score, string reasoning = "ok") => new()
    {
        Outcome = LlmJudgeOutcome.Parsed,
        Score = score,
        Reasoning = reasoning,
        RawOutput = $"{{\"score\":{score},\"reasoning\":\"{reasoning}\"}}",
        CostUsd = 0.001m,
        InputTokens = 100,
        OutputTokens = 25
    };

    public static LlmJudgeResult Malformed() => new()
    {
        Outcome = LlmJudgeOutcome.Malformed,
        Score = 0.0,
        Reasoning = "Judge returned malformed JSON on both attempts.",
        RawOutput = "garbage",
        CostUsd = 0.001m,
        InputTokens = 100,
        OutputTokens = 25
    };

    public static ILogger<T> Log<T>() => NullLogger<T>.Instance;
}
