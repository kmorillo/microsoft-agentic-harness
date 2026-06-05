using Application.AI.Common.CQRS.SkillTraining.SlowUpdate;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.CQRS.SkillTraining;

public class SlowUpdateCommandHandlerTests
{
    private static readonly SlowUpdateCommandHandler Sut = new();

    private static RolloutResult R(string id, double hard) =>
        new() { ItemId = id, Hard = hard, Soft = hard };

    [Fact]
    public async Task Handle_ClassifiesEachCategoryCorrectly()
    {
        var prior = new[]
        {
            R("a", 0.0),  // fail
            R("b", 1.0),  // pass
            R("c", 0.0),  // fail
            R("d", 1.0)   // pass
        };
        var current = new[]
        {
            R("a", 1.0),  // a: 0→1 = improved
            R("b", 0.0),  // b: 1→0 = regressed
            R("c", 0.0),  // c: 0→0 = persistent_fail
            R("d", 1.0)   // d: 1→1 = stable_success
        };

        var result = await Sut.Handle(
            new SlowUpdateCommand { PriorRollouts = prior, CurrentRollouts = current },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var a = result.Value!;
        a.Improved.Should().Be(1);
        a.Regressed.Should().Be(1);
        a.PersistentFail.Should().Be(1);
        a.StableSuccess.Should().Be(1);
        a.Total.Should().Be(4);
        a.Guidance.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_RegressedExceedsImproved_FlagsForgettingInGuidance()
    {
        var prior = new[] { R("a", 1.0), R("b", 1.0), R("c", 0.0) };
        var current = new[] { R("a", 0.0), R("b", 0.0), R("c", 1.0) };  // 1 improved, 2 regressed

        var result = await Sut.Handle(
            new SlowUpdateCommand { PriorRollouts = prior, CurrentRollouts = current },
            CancellationToken.None);

        result.Value!.Guidance.Should().Contain("regressing");
    }

    [Fact]
    public async Task Handle_NoOverlap_ReturnsZerosAndExplainsInGuidance()
    {
        var prior = new[] { R("a", 1.0) };
        var current = new[] { R("z", 1.0) };

        var result = await Sut.Handle(
            new SlowUpdateCommand { PriorRollouts = prior, CurrentRollouts = current },
            CancellationToken.None);

        result.Value!.Total.Should().Be(0);
        result.Value.Guidance.Should().Contain("no paired items");
    }

    [Fact]
    public async Task Handle_OnlyOverlappingIdsCounted()
    {
        var prior = new[] { R("a", 1.0), R("b", 0.0) };
        var current = new[] { R("a", 1.0), R("c", 1.0) };  // only "a" overlaps

        var result = await Sut.Handle(
            new SlowUpdateCommand { PriorRollouts = prior, CurrentRollouts = current },
            CancellationToken.None);

        result.Value!.Total.Should().Be(1);
        result.Value.StableSuccess.Should().Be(1);
    }
}
