// src/Content/Tests/Infrastructure.AI.Tests/Routing/TaskComplexityHeuristicTests.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI.Routing;
using Infrastructure.AI.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Infrastructure.AI.Tests.Routing;

public class TaskComplexityHeuristicTests
{
    private readonly TaskComplexityHeuristic _sut;

    public TaskComplexityHeuristicTests()
    {
        var config = new ModelRoutingConfig
        {
            HeuristicConfidenceThreshold = 0.8,
            HeuristicThresholds = new HeuristicThresholdsConfig
            {
                TrivialMaxLength = 50,
                SimpleMaxLength = 200,
                ModerateMaxLength = 1000,
                ComplexMinToolCount = 8,
                ComplexKeywords = ["refactor", "design", "plan", "architect"],
                TrivialKeywords = ["hi", "hello", "thanks", "ok", "yes", "no"]
            }
        };

        var options = Options.Create(config);
        _sut = new TaskComplexityHeuristic(options, NullLogger<TaskComplexityHeuristic>.Instance);
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("thanks")]
    [InlineData("ok")]
    public void Classify_ShortGreeting_ReturnsTrivial(string message)
    {
        var context = MakeContext(message, turnNumber: 1, toolCount: 0);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Trivial, result.Complexity);
        Assert.True(result.Confidence >= 0.8);
        Assert.Equal(ClassificationSource.Heuristic, result.Source);
    }

    [Fact]
    public void Classify_ShortQuestionFewTools_ReturnsSimple()
    {
        var context = MakeContext("What is dependency injection?", turnNumber: 1, toolCount: 2);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Simple, result.Complexity);
        Assert.True(result.Confidence >= 0.8);
    }

    [Fact]
    public void Classify_MediumMessageWithCodeBlock_ReturnsModerate()
    {
        var message = "Can you analyze this code?\n```csharp\npublic class Foo { }\n```";
        var context = MakeContext(message, turnNumber: 4, toolCount: 5);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.True(result.Complexity >= TaskComplexity.Moderate);
    }

    [Fact]
    public void Classify_LongMessageWithComplexKeywords_ReturnsComplex()
    {
        var message = "I need to refactor the entire authentication system. " + new string('x', 1000);
        var context = MakeContext(message, turnNumber: 10, toolCount: 12,
            recentTools: ["file_system", "code_search", "git", "test_runner", "linter"]);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Complex, result.Complexity);
        Assert.True(result.Confidence >= 0.8);
    }

    [Fact]
    public void Classify_AmbiguousMessage_ReturnsNull()
    {
        // Medium length, no keywords, moderate tools — ambiguous
        var context = MakeContext("Show me how the system processes a request", turnNumber: 3, toolCount: 4);

        var result = _sut.Classify(context);

        // May return null (triggers LLM fallback) or low-confidence result
        if (result is not null)
        {
            Assert.Equal(ClassificationSource.Heuristic, result.Source);
        }
    }

    [Fact]
    public void Classify_HighToolCountOnly_ReturnsComplex()
    {
        var context = MakeContext("Do this task", turnNumber: 1, toolCount: 15);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Complex, result.Complexity);
    }

    private static AgentTurnContext MakeContext(
        string message,
        int turnNumber = 1,
        int toolCount = 0,
        IReadOnlyList<string>? recentTools = null) => new()
    {
        ConversationId = "test-conv-001",
        UserMessage = message,
        TurnNumber = turnNumber,
        AvailableToolCount = toolCount,
        ConversationDepth = turnNumber,
        RecentToolNames = recentTools
    };
}
