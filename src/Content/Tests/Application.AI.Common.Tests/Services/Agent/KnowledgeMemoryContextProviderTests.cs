using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Application.AI.Common.Services.Agent;
using Domain.AI.KnowledgeGraph.Models;
using Domain.Common.Config;
using Domain.Common.Config.AI;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Application.AI.Common.Tests.Services.Agent;

public class KnowledgeMemoryContextProviderTests
{
    private static AIContext ContextWithUserMessage(string text, string? instructions = null) => new()
    {
        Instructions = instructions,
        Messages = new List<ChatMessage> { new(ChatRole.User, text) }
    };

    private static GraphNode Fact(string content) => new()
    {
        Id = $"memory:{content.GetHashCode()}",
        Name = "fact-key",
        Type = "Fact",
        Properties = new Dictionary<string, string> { ["content"] = content }
    };

    private static KnowledgeMemoryContextProvider Build(
        IKnowledgeMemory? memory,
        bool enabled = true,
        bool withScope = true)
    {
        IServiceProvider? scopeProvider = null;
        if (withScope)
        {
            var services = new ServiceCollection();
            if (memory is not null)
                services.AddSingleton(memory);
            scopeProvider = services.BuildServiceProvider();
        }

        var ambient = Mock.Of<IAmbientRequestScope>(a => a.Current == scopeProvider);
        var appConfig = new AppConfig { AI = new AIConfig { KnowledgeBridge = new KnowledgeBridgeConfig { Enabled = enabled } } };
        var monitor = Mock.Of<IOptionsMonitor<AppConfig>>(m => m.CurrentValue == appConfig);
        return new KnowledgeMemoryContextProvider(
            ambient, monitor, NullLogger<KnowledgeMemoryContextProvider>.Instance);
    }

    [Fact]
    public async Task RecallAndInject_WithRelevantFacts_AppendsThemToInstructions()
    {
        var memory = new Mock<IKnowledgeMemory>();
        memory.Setup(m => m.RecallAsync("what theme do I like?", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Fact("The user prefers dark mode."), Fact("The user is based in NYC.") });
        var sut = Build(memory.Object);
        var input = ContextWithUserMessage("what theme do I like?", instructions: "You are helpful.");

        var result = await sut.RecallAndInjectAsync(input);

        result.Should().NotBeSameAs(input);
        result.Instructions.Should().Contain("You are helpful.");
        result.Instructions.Should().Contain("Relevant remembered context");
        result.Instructions.Should().Contain("The user prefers dark mode.");
        result.Instructions.Should().Contain("The user is based in NYC.");
        // Messages/tools are passed through untouched.
        result.Messages.Should().BeSameAs(input.Messages);
    }

    [Fact]
    public async Task RecallAndInject_Disabled_ReturnsInputUnchanged()
    {
        var memory = new Mock<IKnowledgeMemory>(MockBehavior.Strict);
        var sut = Build(memory.Object, enabled: false);
        var input = ContextWithUserMessage("anything");

        var result = await sut.RecallAndInjectAsync(input);

        result.Should().BeSameAs(input);
        memory.Verify(m => m.RecallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecallAndInject_NoAmbientScope_ReturnsInputUnchanged()
    {
        // No request scope established (e.g. background work) → cannot resolve tenant-aware memory.
        var sut = Build(memory: null, withScope: false);
        var input = ContextWithUserMessage("anything");

        var result = await sut.RecallAndInjectAsync(input);

        result.Should().BeSameAs(input);
    }

    [Fact]
    public async Task RecallAndInject_NoUserMessage_ReturnsInputUnchanged()
    {
        var memory = new Mock<IKnowledgeMemory>(MockBehavior.Strict);
        var sut = Build(memory.Object);
        var input = new AIContext { Messages = new List<ChatMessage> { new(ChatRole.System, "system only") } };

        var result = await sut.RecallAndInjectAsync(input);

        result.Should().BeSameAs(input);
        memory.Verify(m => m.RecallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecallAndInject_NoRelevantFacts_ReturnsInputUnchanged()
    {
        var memory = new Mock<IKnowledgeMemory>();
        memory.Setup(m => m.RecallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GraphNode>());
        var sut = Build(memory.Object);
        var input = ContextWithUserMessage("anything", instructions: "keep me");

        var result = await sut.RecallAndInjectAsync(input);

        result.Should().BeSameAs(input);
    }

    [Fact]
    public async Task RecallAndInject_MemoryThrows_ReturnsInputUnchanged()
    {
        // Memory is an enhancement, never a hard dependency: a recall failure must not break the turn.
        var memory = new Mock<IKnowledgeMemory>();
        memory.Setup(m => m.RecallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("graph down"));
        var sut = Build(memory.Object);
        var input = ContextWithUserMessage("anything", instructions: "keep me");

        var result = await sut.RecallAndInjectAsync(input);

        result.Should().BeSameAs(input);
    }

    [Fact]
    public async Task RecallAndInject_NoExistingInstructions_UsesFactsBlockAlone()
    {
        var memory = new Mock<IKnowledgeMemory>();
        memory.Setup(m => m.RecallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Fact("Likes terse answers.") });
        var sut = Build(memory.Object);
        var input = ContextWithUserMessage("style?"); // no instructions

        var result = await sut.RecallAndInjectAsync(input);

        result.Instructions.Should().StartWith("## Relevant remembered context");
        result.Instructions.Should().Contain("Likes terse answers.");
    }
}
