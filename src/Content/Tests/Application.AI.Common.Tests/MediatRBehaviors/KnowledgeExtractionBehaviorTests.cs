using Application.AI.Common.Interfaces.KnowledgeGraph;
using Application.AI.Common.MediatRBehaviors;
using Application.Core.CQRS.Agents.ExecuteAgentTurn;
using Domain.AI.KnowledgeGraph.Models;
using Domain.Common.Config.AI;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Application.AI.Common.Tests.MediatRBehaviors;

public class KnowledgeExtractionBehaviorTests
{
    private readonly Mock<IConversationFactExtractor> _mockExtractor = new();
    private readonly Mock<IKnowledgeMemory> _mockMemory = new();
    private readonly KnowledgeBridgeConfig _config;

    public KnowledgeExtractionBehaviorTests()
    {
        _config = new KnowledgeBridgeConfig { Enabled = true };
    }

    [Fact]
    public async Task Handle_NonAgentTurnRequest_PassesThrough()
    {
        var behavior = CreateBehavior<NonAgentRequest, string>();

        var result = await behavior.Handle(
            new NonAgentRequest(),
            () => Task.FromResult("passthrough"),
            CancellationToken.None);

        result.Should().Be("passthrough");
        _mockExtractor.Verify(
            e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Disabled_SkipsExtraction()
    {
        _config.Enabled = false;
        var behavior = CreateAgentTurnBehavior();
        var command = CreateCommand("analyze this code");
        var response = CreateSuccessResponse("Here's my analysis...");

        var result = await behavior.Handle(command, () => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
        _mockExtractor.Verify(
            e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_FailedTurn_SkipsExtraction()
    {
        var behavior = CreateAgentTurnBehavior();
        var command = CreateCommand("do something");
        var response = new AgentTurnResult
        {
            Success = false,
            Response = "",
            UpdatedHistory = [],
            Error = "Agent error"
        };

        var result = await behavior.Handle(command, () => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
        _mockExtractor.Verify(
            e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmptyResponse_SkipsExtraction()
    {
        var behavior = CreateAgentTurnBehavior();
        var command = CreateCommand("hello");
        var response = CreateSuccessResponse("");

        var result = await behavior.Handle(command, () => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
        _mockExtractor.Verify(
            e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SuccessfulTurn_ExtractsAndRemembersFacts()
    {
        var facts = new List<ConversationFact>
        {
            new() { Key = "conv-1:3:0", Content = "User prefers PostgreSQL", EntityType = "Preference", Confidence = 0.9 },
            new() { Key = "conv-1:3:1", Content = "Deadline is June 15", EntityType = "Decision", Confidence = 0.85 }
        };

        _mockExtractor
            .Setup(e => e.ExtractAsync("use PostgreSQL", "Noted.", "conv-1", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var behavior = CreateAgentTurnBehavior();
        var command = CreateCommand("use PostgreSQL", "conv-1", 3);
        var response = CreateSuccessResponse("Noted.");

        var result = await behavior.Handle(command, () => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);

        // Wait briefly for the fire-and-forget task to complete
        await Task.Delay(200);

        _mockMemory.Verify(m => m.RememberAsync("conv-1:3:0", "User prefers PostgreSQL", "Preference", It.IsAny<CancellationToken>()), Times.Once);
        _mockMemory.Verify(m => m.RememberAsync("conv-1:3:1", "Deadline is June 15", "Decision", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExtractorThrows_ResponseStillReturned()
    {
        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM down"));

        var behavior = CreateAgentTurnBehavior();
        var command = CreateCommand("analyze this");
        var response = CreateSuccessResponse("Analysis complete.");

        var result = await behavior.Handle(command, () => Task.FromResult(response), CancellationToken.None);

        result.Should().BeSameAs(response);
        // No exception propagated — fire-and-forget absorbed it
    }

    [Fact]
    public async Task Handle_RememberAsyncThrows_ContinuesWithRemainingFacts()
    {
        var facts = new List<ConversationFact>
        {
            new() { Key = "conv-1:1:0", Content = "Fact A", Confidence = 0.9 },
            new() { Key = "conv-1:1:1", Content = "Fact B", Confidence = 0.85 }
        };

        _mockExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        _mockMemory
            .Setup(m => m.RememberAsync("conv-1:1:0", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("graph full"));

        var behavior = CreateAgentTurnBehavior();
        var command = CreateCommand("msg", "conv-1", 1);
        var response = CreateSuccessResponse("resp");

        await behavior.Handle(command, () => Task.FromResult(response), CancellationToken.None);
        await Task.Delay(200);

        // Second fact should still be remembered despite first one throwing
        _mockMemory.Verify(m => m.RememberAsync("conv-1:1:1", "Fact B", "Fact", It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Helpers ---

    private KnowledgeExtractionBehavior<TRequest, TResponse> CreateBehavior<TRequest, TResponse>()
        where TRequest : notnull
    {
        return new KnowledgeExtractionBehavior<TRequest, TResponse>(
            _mockExtractor.Object,
            _mockMemory.Object,
            Options.Create(_config),
            NullLogger<KnowledgeExtractionBehavior<TRequest, TResponse>>.Instance);
    }

    private KnowledgeExtractionBehavior<ExecuteAgentTurnCommand, AgentTurnResult> CreateAgentTurnBehavior()
    {
        return new KnowledgeExtractionBehavior<ExecuteAgentTurnCommand, AgentTurnResult>(
            _mockExtractor.Object,
            _mockMemory.Object,
            Options.Create(_config),
            NullLogger<KnowledgeExtractionBehavior<ExecuteAgentTurnCommand, AgentTurnResult>>.Instance);
    }

    private static ExecuteAgentTurnCommand CreateCommand(
        string userMessage, string conversationId = "conv-1", int turnNumber = 1) =>
        new()
        {
            AgentName = "test-agent",
            UserMessage = userMessage,
            ConversationId = conversationId,
            TurnNumber = turnNumber
        };

    private static AgentTurnResult CreateSuccessResponse(string response) =>
        new()
        {
            Success = true,
            Response = response,
            UpdatedHistory = []
        };

    // Test-local request type that is NOT ExecuteAgentTurnCommand
    private record NonAgentRequest : IRequest<string>;
}
