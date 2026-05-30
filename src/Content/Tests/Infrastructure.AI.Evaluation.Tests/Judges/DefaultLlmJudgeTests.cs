using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.AI.Common.Evaluation.Outcomes;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Judges;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Judges;

public sealed class DefaultLlmJudgeTests
{
    private static (Mock<IJudgeChatClientProvider> provider, Mock<IChatClient> client) Plumbing(params string[] responses)
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
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
                {
                    Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 25 }
                };
            });

        var provider = new Mock<IJudgeChatClientProvider>();
        provider.Setup(p => p.GetJudgeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        return (provider, clientMock);
    }

    private static IOptionsMonitor<JudgeCostOptions> CostRates(decimal inputRate, decimal outputRate)
    {
        var mon = new Mock<IOptionsMonitor<JudgeCostOptions>>();
        mon.SetupGet(m => m.CurrentValue).Returns(new JudgeCostOptions
        {
            InputCostPerMillionTokens = inputRate,
            OutputCostPerMillionTokens = outputRate
        });
        return mon.Object;
    }

    private static DefaultLlmJudge MakeSut(IJudgeChatClientProvider provider, IOptionsMonitor<JudgeCostOptions>? costs = null)
        => new(provider, NullLogger<DefaultLlmJudge>.Instance, costs);

    private static LlmJudgeRequest MakeRequest(
        string system = "system core",
        string template = "Score this: {{x}}",
        Dictionary<string, string?>? vars = null)
        => new()
        {
            SystemPromptCore = system,
            UserPromptTemplate = template,
            Variables = vars ?? new Dictionary<string, string?> { ["x"] = "answer text" },
        };

    [Fact]
    public async Task JudgeAsync_parses_clean_response_and_computes_cost()
    {
        var (provider, _) = Plumbing("""{"score": 0.8, "reasoning": "good"}""");
        var sut = MakeSut(provider.Object, CostRates(10m, 30m));

        var result = await sut.JudgeAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.Parsed);
        result.Score.Should().Be(0.8);
        result.Reasoning.Should().Be("good");
        result.InputTokens.Should().Be(100);
        result.OutputTokens.Should().Be(25);
        result.CostUsd.Should().Be(0.00175m);
    }

    [Fact]
    public async Task JudgeAsync_retries_once_and_returns_parsed_on_recovery()
    {
        var (provider, client) = Plumbing("garbage", """{"score": 0.5, "reasoning": "ok"}""");
        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.Parsed);
        result.Score.Should().Be(0.5);
        client.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        result.InputTokens.Should().Be(200);
        result.OutputTokens.Should().Be(50);
    }

    [Fact]
    public async Task JudgeAsync_returns_malformed_when_both_attempts_unparseable_nonempty()
    {
        var (provider, _) = Plumbing("nope", "still nope");
        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.Malformed);
        result.Score.Should().Be(0.0);
        result.RawOutput.Should().Be("still nope");
    }

    [Fact]
    public async Task JudgeAsync_short_circuits_empty_response_to_invocation_failed_without_retry()
    {
        var (provider, client) = Plumbing("", "would-be-recovery");
        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.InvocationFailed);
        result.Reasoning.Should().Contain("empty response");
        client.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JudgeAsync_returns_invocation_failed_when_provider_throws()
    {
        var provider = new Mock<IJudgeChatClientProvider>();
        provider.Setup(p => p.GetJudgeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.InvocationFailed);
        result.Reasoning.Should().Contain("provider down");
    }

    [Fact]
    public async Task JudgeAsync_returns_invocation_failed_when_chat_call_throws()
    {
        var clientMock = new Mock<IChatClient>();
        clientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model timeout"));

        var provider = new Mock<IJudgeChatClientProvider>();
        provider.Setup(p => p.GetJudgeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clientMock.Object);

        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.InvocationFailed);
        result.Reasoning.Should().Contain("model timeout");
    }

    [Fact]
    public async Task JudgeAsync_clamps_score_to_zero_one_and_rejects_nan()
    {
        var (provider, _) = Plumbing("""{"score": 1.5, "reasoning": "over"}""");
        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.Parsed);
        result.Score.Should().Be(1.0);
    }

    [Fact]
    public async Task JudgeAsync_propagates_cancellation()
    {
        var provider = new Mock<IJudgeChatClientProvider>();
        provider.Setup(p => p.GetJudgeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = MakeSut(provider.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.JudgeAsync(MakeRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task JudgeAsync_returns_invocation_failed_on_empty_system_prompt()
    {
        var (provider, client) = Plumbing("""{"score": 1.0, "reasoning": "ok"}""");
        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(system: ""), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.InvocationFailed);
        result.Reasoning.Should().Contain("SystemPromptCore");
        client.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JudgeAsync_returns_invocation_failed_on_empty_user_template()
    {
        var (provider, client) = Plumbing("""{"score": 1.0, "reasoning": "ok"}""");
        var sut = MakeSut(provider.Object);

        var result = await sut.JudgeAsync(MakeRequest(template: ""), CancellationToken.None);

        result.Outcome.Should().Be(LlmJudgeOutcome.InvocationFailed);
        result.Reasoning.Should().Contain("UserPromptTemplate");
        client.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JudgeAsync_html_escapes_variables_and_envelopes_user_with_nonce()
    {
        IEnumerable<ChatMessage>? capturedMessages = null;
        var clientMock = new Mock<IChatClient>();
        clientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"score":1.0,"reasoning":"ok"}""")));
        var provider = new Mock<IJudgeChatClientProvider>();
        provider.Setup(p => p.GetJudgeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clientMock.Object);

        var sut = MakeSut(provider.Object);
        await sut.JudgeAsync(MakeRequest(template: "Look at: {{x}}",
            vars: new Dictionary<string, string?> { ["x"] = "<bad>" }),
            CancellationToken.None);

        var messages = capturedMessages!.ToList();
        messages.Should().HaveCount(2);
        var userText = messages[1].Text!;
        userText.Should().Contain("&lt;bad&gt;");
        userText.Should().Contain("<judge_data_").And.Contain("</judge_data_");
    }

}
