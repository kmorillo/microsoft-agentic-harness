using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI;
using FluentAssertions;
using Infrastructure.AI.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Infrastructure.AI.RAG.Tests.QueryTransform;

public sealed class QueryComplexityClassifierTests
{
    private readonly Mock<IModelRouter> _mockRouter = new();
    private readonly Mock<IChatClient> _mockChatClient = new();

    public QueryComplexityClassifierTests()
    {
        _mockRouter
            .Setup(r => r.RouteOperationAsync("complexity_classification", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelRoutingDecision
            {
                Client = _mockChatClient.Object,
                SelectedTier = new ModelTier
                {
                    Name = "standard",
                    ClientType = AIAgentFrameworkClientType.AzureOpenAI,
                    DeploymentName = "gpt-4o"
                },
                Complexity = TaskComplexity.Moderate,
                Source = ClassificationSource.Heuristic,
                Confidence = 1.0,
                Reasoning = string.Empty,
            });
    }

    private TaskComplexityClassifier CreateClassifier()
        => new(
            _mockRouter.Object,
            Mock.Of<ILogger<TaskComplexityClassifier>>());

    private void SetupChatResponse(string jsonResponse)
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, jsonResponse)));
    }

    [Fact]
    public async Task ClassifyAsync_TrivialQuery_ReturnsTrivial()
    {
        SetupChatResponse("""{"complexity":"trivial","confidence":0.95,"reasoning":"General knowledge question"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            new AgentTurnContext { ConversationId = "test", UserMessage = "What is the capital of France?", TurnNumber = 1 });

        result.Complexity.Should().Be(TaskComplexity.Trivial);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.9);
        result.SkipRetrieval.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyAsync_SimpleQuery_ReturnsSimple()
    {
        SetupChatResponse("""{"complexity":"simple","confidence":0.85,"reasoning":"Direct factual lookup"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            new AgentTurnContext { ConversationId = "test", UserMessage = "What chunking strategies are available?", TurnNumber = 1 });

        result.Complexity.Should().Be(TaskComplexity.Simple);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.8);
        result.SkipRetrieval.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_ModerateQuery_ReturnsModerate()
    {
        SetupChatResponse("""{"complexity":"moderate","confidence":0.8,"reasoning":"Requires cross-referencing"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            new AgentTurnContext { ConversationId = "test", UserMessage = "Compare the CRAG and Self-RAG approaches in our pipeline", TurnNumber = 1 });

        result.Complexity.Should().Be(TaskComplexity.Moderate);
    }

    [Fact]
    public async Task ClassifyAsync_ComplexQuery_ReturnsComplex()
    {
        SetupChatResponse("""{"complexity":"complex","confidence":0.75,"reasoning":"Multi-hop reasoning needed"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            new AgentTurnContext { ConversationId = "test", UserMessage = "Based on the architecture docs and the deployment guide, what changes are needed to support multi-tenant GraphRAG?", TurnNumber = 1 });

        result.Complexity.Should().Be(TaskComplexity.Complex);
    }

    [Fact]
    public async Task ClassifyAsync_InvalidJson_FallsBackToModerate()
    {
        SetupChatResponse("I can't classify this properly");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            new AgentTurnContext { ConversationId = "test", UserMessage = "Some query", TurnNumber = 1 });

        result.Complexity.Should().Be(TaskComplexity.Moderate);
        result.Confidence.Should().Be(0.5);
    }

    [Fact]
    public async Task ClassifyAsync_LlmThrows_FallsBackToModerate()
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            new AgentTurnContext { ConversationId = "test", UserMessage = "Some query", TurnNumber = 1 });

        result.Complexity.Should().Be(TaskComplexity.Moderate);
        result.Confidence.Should().Be(0.5);
    }

    [Fact]
    public async Task ClassifyAsync_ConfidenceClamped_StaysInRange()
    {
        SetupChatResponse("""{"complexity":"simple","confidence":1.5,"reasoning":"Over-confident"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            new AgentTurnContext { ConversationId = "test", UserMessage = "Test query", TurnNumber = 1 });

        result.Confidence.Should().BeInRange(0.0, 1.0);
    }
}
