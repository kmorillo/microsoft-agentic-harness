using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class RolloutBatchScorerTests
{
    [Fact]
    public void Score_EmptyList_ReturnsZeros()
    {
        var (hard, soft) = RolloutBatchScorer.Score([]);
        hard.Should().Be(0.0);
        soft.Should().Be(0.0);
    }

    [Fact]
    public void Score_ComputesMeanHardAndSoft()
    {
        IReadOnlyList<RolloutResult> rs =
        [
            new RolloutResult { ItemId = "a", Hard = 1.0, Soft = 0.8 },
            new RolloutResult { ItemId = "b", Hard = 0.0, Soft = 0.4 },
            new RolloutResult { ItemId = "c", Hard = 1.0, Soft = 1.0 }
        ];

        var (hard, soft) = RolloutBatchScorer.Score(rs);

        hard.Should().BeApproximately(2.0 / 3.0, 1e-9);
        soft.Should().BeApproximately((0.8 + 0.4 + 1.0) / 3.0, 1e-9);
    }

    [Fact]
    public void Score_NullInput_Throws()
    {
        var act = () => RolloutBatchScorer.Score(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
