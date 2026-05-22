using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Xunit;

namespace Domain.AI.Tests.Routing;

public class TaskComplexityAssessmentTests
{
    [Fact]
    public void SkipRetrieval_Trivial_ReturnsTrue()
    {
        var assessment = new TaskComplexityAssessment
        {
            Complexity = TaskComplexity.Trivial,
            Confidence = 0.95,
            Source = ClassificationSource.Heuristic
        };

        Assert.True(assessment.SkipRetrieval);
    }

    [Theory]
    [InlineData(TaskComplexity.Simple)]
    [InlineData(TaskComplexity.Moderate)]
    [InlineData(TaskComplexity.Complex)]
    public void SkipRetrieval_NonTrivial_ReturnsFalse(TaskComplexity complexity)
    {
        var assessment = new TaskComplexityAssessment
        {
            Complexity = complexity,
            Confidence = 0.9,
            Source = ClassificationSource.Heuristic
        };

        Assert.False(assessment.SkipRetrieval);
    }

    [Fact]
    public void TaskComplexity_ValuesAreOrdered()
    {
        Assert.True(TaskComplexity.Trivial < TaskComplexity.Simple);
        Assert.True(TaskComplexity.Simple < TaskComplexity.Moderate);
        Assert.True(TaskComplexity.Moderate < TaskComplexity.Complex);
    }

    [Fact]
    public void ModelTier_RecordEquality()
    {
        var tier1 = new ModelTier
        {
            Name = "economy",
            ClientType = Domain.Common.Config.AI.AIAgentFrameworkClientType.OpenAI,
            DeploymentName = "gpt-4o-mini",
            EstimatedCostPer1KTokens = 0.00015m
        };

        var tier2 = tier1 with { Name = "standard" };

        Assert.NotEqual(tier1, tier2);
        Assert.Equal("economy", tier1.Name);
        Assert.Equal("standard", tier2.Name);
    }
}
