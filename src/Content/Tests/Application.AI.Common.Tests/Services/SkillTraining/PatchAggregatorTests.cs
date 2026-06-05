using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class PatchAggregatorTests
{
    private static readonly PatchAggregator Sut = new();

    [Fact]
    public void Aggregate_EmptyList_ReturnsEmptyPatch()
    {
        var result = Sut.Aggregate([]);

        result.Edits.Should().BeEmpty();
        result.Reasoning.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_SinglePatch_PreservesEditsAndSeedsSupport()
    {
        var patch = new Patch
        {
            Reasoning = "first pass",
            Edits =
            [
                new Edit { Op = EditOp.Append, Content = "new rule" }
            ]
        };

        var result = Sut.Aggregate([patch]);

        result.Edits.Should().HaveCount(1);
        result.Edits[0].SupportCount.Should().Be(1, because: "missing support seeds to 1");
        result.Reasoning.Should().Be("first pass");
    }

    [Fact]
    public void Aggregate_IdenticalEdits_AcrossPatches_AreMerged_SupportSummed()
    {
        var e = new Edit { Op = EditOp.Append, Content = "rule", SupportCount = 1 };
        var p1 = new Patch { Edits = [e], Reasoning = "r1" };
        var p2 = new Patch { Edits = [e], Reasoning = "r2" };
        var p3 = new Patch { Edits = [e] };

        var result = Sut.Aggregate([p1, p2, p3]);

        result.Edits.Should().HaveCount(1);
        result.Edits[0].SupportCount.Should().Be(3);
        result.Edits[0].MergeLevel.Should().BeGreaterThan(0,
            because: "merged edits get an incremented merge level");
        result.Reasoning.Should().Be("r1\n---\nr2");
    }

    [Fact]
    public void Aggregate_DistinctEdits_ArePreservedSeparately_InFirstSeenOrder()
    {
        var a = new Edit { Op = EditOp.Append, Content = "a" };
        var b = new Edit { Op = EditOp.Append, Content = "b" };
        var c = new Edit { Op = EditOp.Replace, Target = "x", Content = "y" };
        var p1 = new Patch { Edits = [a, c] };
        var p2 = new Patch { Edits = [b, a] };

        var result = Sut.Aggregate([p1, p2]);

        result.Edits.Should().HaveCount(3);
        result.Edits[0].Content.Should().Be("a");
        result.Edits[1].Target.Should().Be("x");
        result.Edits[2].Content.Should().Be("b");
        result.Edits[0].SupportCount.Should().Be(2, because: "edit 'a' appeared twice");
    }

    [Fact]
    public void Aggregate_DistinguishesByAllThreeKeyFields()
    {
        var p1 = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Append, Content = "x", Target = "" },
                new Edit { Op = EditOp.Replace, Content = "x", Target = "y" },
                new Edit { Op = EditOp.Replace, Content = "x", Target = "z" }
            ]
        };

        var result = Sut.Aggregate([p1]);

        result.Edits.Should().HaveCount(3,
            because: "Op, Target, Content tuple is the equivalence key — none collapse");
    }

    [Fact]
    public void Aggregate_SkipsNullPatchEntries()
    {
        var patch = new Patch { Edits = [new Edit { Op = EditOp.Append, Content = "x" }] };

        var result = Sut.Aggregate([patch, null!, patch]);

        result.Edits.Should().HaveCount(1);
        result.Edits[0].SupportCount.Should().Be(2);
    }

    [Fact]
    public void Aggregate_NullList_Throws()
    {
        var act = () => Sut.Aggregate(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
