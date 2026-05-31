using Application.AI.Common.CQRS.Prompts.ReplayTraceWithPromptVersion;
using Application.AI.Common.Interfaces;
using Application.AI.Common.Prompts.Exceptions;
using Application.AI.Common.Prompts.Interfaces;
using Application.AI.Common.Prompts.Models;
using Domain.AI.Prompts;
using Domain.Common.Config.AI;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Application.AI.Common.Tests.CQRS.Prompts;

/// <summary>
/// Coverage for <see cref="ReplayTraceWithPromptVersionCommandHandler"/>. Verifies
/// success path, every Result&lt;T&gt; failure branch, and the temperature-zero invariant.
/// </summary>
public sealed class ReplayTraceWithPromptVersionCommandHandlerTests
{
    private readonly Mock<IPromptUsageStore> _store = new();
    private readonly Mock<IPromptRegistry> _registry = new();
    private readonly Mock<IPromptRenderer> _renderer = new();
    private readonly Mock<IChatClientFactory> _chatFactory = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly ReplayTraceWithPromptVersionCommandHandler _sut;

    public ReplayTraceWithPromptVersionCommandHandlerTests()
    {
        _chatFactory
            .Setup(f => f.GetChatClientAsync(
                It.IsAny<AIAgentFrameworkClientType>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_chat.Object);

        _renderer
            .Setup(r => r.RenderAsync(
                It.IsAny<PromptDescriptor>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PromptDescriptor d, IReadOnlyDictionary<string, object?> _, CancellationToken __)
                => new RenderedPrompt { Source = d, Body = $"rendered-{d.Identifier}" });

        _sut = new ReplayTraceWithPromptVersionCommandHandler(
            _store.Object,
            _registry.Object,
            _renderer.Object,
            _chatFactory.Object,
            NullLogger<ReplayTraceWithPromptVersionCommandHandler>.Instance);
    }

    private static PromptDescriptor Desc(string name = "p", int major = 1, int minor = 0, string? hash = null) => new()
    {
        Name = name,
        Version = new PromptVersion(major, minor),
        ContentHash = hash ?? $"hash-{major}-{minor}",
        Body = $"body-{major}.{minor}",
    };

    private static PromptUsageRecord UsageRow(string trace, PromptDescriptor descriptor) => new()
    {
        Descriptor = descriptor,
        TraceId = trace,
        RecordedAtUtc = DateTimeOffset.UtcNow,
    };

    private static ReplayTraceWithPromptVersionCommand Cmd(
        string trace = "trace-1",
        string name = "p",
        int targetMajor = 2,
        int targetMinor = 0,
        int? maxTokens = null)
        => new()
        {
            TraceId = trace,
            PromptName = name,
            TargetVersion = new PromptVersion(targetMajor, targetMinor),
            Variables = new Dictionary<string, object?> { ["x"] = 1 },
            ChatClientType = AIAgentFrameworkClientType.OpenAI,
            Deployment = "gpt-4o-mini",
            MaxOutputTokens = maxTokens,
        };

    [Fact]
    public async Task Handle_SuccessPath_ReturnsRenderedDescriptorsAndOutput()
    {
        var original = Desc("p", 1, 0);
        var target = Desc("p", 2, 0);

        _store
            .Setup(s => s.QueryByTraceIdAsync("trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([UsageRow("trace-1", original)]);

        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(2, 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        _chat
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "replayed output")));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TargetOutput.Should().Be("replayed output");
        result.Value.OriginalDescriptor.Version.Should().Be(new PromptVersion(1, 0));
        result.Value.TargetDescriptor.Version.Should().Be(new PromptVersion(2, 0));
        result.Value.OriginalRenderedPrompt.Body.Should().Be("rendered-p@v1.0");
        result.Value.TargetRenderedPrompt.Body.Should().Be("rendered-p@v2.0");
        result.Value.ContentHashChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ForcesTemperatureZeroAndDefaultMaxTokens()
    {
        var original = Desc("p", 1, 0);
        var target = Desc("p", 2, 0);

        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UsageRow("trace-1", original)]);

        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(2, 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        ChatOptions? capturedOptions = null;
        _chat
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        await _sut.Handle(Cmd(), CancellationToken.None);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.Temperature.Should().Be(ReplayTraceWithPromptVersionCommandHandler.ReplayTemperature);
        capturedOptions.MaxOutputTokens.Should().Be(ReplayTraceWithPromptVersionCommandHandler.DefaultMaxOutputTokens);
    }

    [Fact]
    public async Task Handle_HonorsExplicitMaxOutputTokens()
    {
        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UsageRow("trace-1", Desc("p", 1, 0))]);
        _registry.Setup(r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>())).ReturnsAsync(Desc("p", 1, 0));
        _registry.Setup(r => r.GetAsync("p", new PromptVersion(2, 0), It.IsAny<CancellationToken>())).ReturnsAsync(Desc("p", 2, 0));

        ChatOptions? capturedOptions = null;
        _chat
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        await _sut.Handle(Cmd(maxTokens: 512), CancellationToken.None);

        capturedOptions!.MaxOutputTokens.Should().Be(512);
    }

    [Fact]
    public async Task Handle_PicksHighestVersion_WhenTraceContainsMultipleUsesOfSamePrompt()
    {
        var v1 = Desc("p", 1, 0);
        var v15 = Desc("p", 1, 5);
        var target = Desc("p", 2, 0);

        _store
            .Setup(s => s.QueryByTraceIdAsync("trace-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptUsageRecord[]
            {
                UsageRow("trace-1", v1),
                UsageRow("trace-1", v15),
            });

        _registry.Setup(r => r.GetAsync("p", new PromptVersion(1, 5), It.IsAny<CancellationToken>())).ReturnsAsync(v15);
        _registry.Setup(r => r.GetAsync("p", new PromptVersion(2, 0), It.IsAny<CancellationToken>())).ReturnsAsync(target);

        _chat
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OriginalDescriptor.Version.Should().Be(new PromptVersion(1, 5));
        _registry.Verify(
            r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoUsageRowForTrace_ReturnsNotFound()
    {
        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().NotBeNull();
        result.Errors.Should().Contain(e => e.Contains("trace") && e.Contains("trace-1"));
    }

    [Fact]
    public async Task Handle_OriginalVersionDeletedFromRegistry_ReturnsFailWithRecoveryMessage()
    {
        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UsageRow("trace-1", Desc("p", 1, 0))]);

        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("v1.0 was deleted"));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("no longer present"));
        _chat.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_TargetVersionNotInRegistry_ReturnsNotFound()
    {
        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UsageRow("trace-1", Desc("p", 1, 0))]);
        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Desc("p", 1, 0));
        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(2, 0), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("v2.0 doesn't exist"));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Target prompt"));
        _chat.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_RegistryUnavailable_ReturnsFailWithoutLlmCall()
    {
        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UsageRow("trace-1", Desc("p", 1, 0))]);
        _registry
            .Setup(r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PromptRegistryUnavailableException("p", "offline", new IOException("blip")));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("registry unavailable"));
        _chat.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_LlmThrows_ReturnsFail()
    {
        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UsageRow("trace-1", Desc("p", 1, 0))]);
        _registry.Setup(r => r.GetAsync("p", new PromptVersion(1, 0), It.IsAny<CancellationToken>())).ReturnsAsync(Desc("p", 1, 0));
        _registry.Setup(r => r.GetAsync("p", new PromptVersion(2, 0), It.IsAny<CancellationToken>())).ReturnsAsync(Desc("p", 2, 0));

        _chat
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rate limited"));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Replay LLM call failed"));
    }

    [Fact]
    public async Task Handle_OperationCancelledDuringStoreQuery_Propagates()
    {
        _store
            .Setup(s => s.QueryByTraceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Func<Task> act = () => _sut.Handle(Cmd(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
