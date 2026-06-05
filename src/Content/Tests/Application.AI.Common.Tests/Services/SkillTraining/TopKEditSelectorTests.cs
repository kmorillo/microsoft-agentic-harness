using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class TopKEditSelectorTests
{
    private static readonly TopKEditSelector Sut = new();

    [Fact]
    public void SelectTopK_KZero_ReturnsPatchWithNoEdits()
    {
        var patch = NewPatch(("a", 5), ("b", 3));

        var result = Sut.SelectTopK(patch, 0);

        result.Edits.Should().BeEmpty();
    }

    [Fact]
    public void SelectTopK_KGreaterThanEdits_ReturnsAll_RankedBySupport()
    {
        var patch = NewPatch(("a", 3), ("b", 7), ("c", 5));

        var result = Sut.SelectTopK(patch, 10);

        result.Edits.Select(e => e.Content).Should().ContainInOrder("b", "c", "a");
    }

    [Fact]
    public void SelectTopK_ClipsToK_KeepingHighestSupport()
    {
        var patch = NewPatch(("a", 1), ("b", 8), ("c", 4), ("d", 2));

        var result = Sut.SelectTopK(patch, 2);

        result.Edits.Should().HaveCount(2);
        result.Edits.Select(e => e.Content).Should().ContainInOrder("b", "c");
    }

    [Fact]
    public void SelectTopK_TieOnSupport_BreaksByMergeLevel_Descending()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Append, Content = "a", SupportCount = 3, MergeLevel = 0 },
                new Edit { Op = EditOp.Append, Content = "b", SupportCount = 3, MergeLevel = 2 },
                new Edit { Op = EditOp.Append, Content = "c", SupportCount = 3, MergeLevel = 1 }
            ]
        };

        var result = Sut.SelectTopK(patch, 3);

        result.Edits.Select(e => e.Content).Should().ContainInOrder("b", "c", "a");
    }

    [Fact]
    public void SelectTopK_TieOnSupportAndMerge_StableByInsertionOrder()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Append, Content = "first" },
                new Edit { Op = EditOp.Append, Content = "second" },
                new Edit { Op = EditOp.Append, Content = "third" }
            ]
        };

        var result = Sut.SelectTopK(patch, 2);

        result.Edits.Select(e => e.Content).Should().ContainInOrder("first", "second");
    }

    [Fact]
    public void SelectTopK_NegativeK_Throws()
    {
        var act = () => Sut.SelectTopK(NewPatch(("a", 1)), -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SelectTopK_NullPatch_Throws()
    {
        var act = () => Sut.SelectTopK(null!, 5);
        act.Should().Throw<ArgumentNullException>();
    }

    private static Patch NewPatch(params (string Content, int Support)[] edits) => new()
    {
        Edits = edits.Select(t => new Edit
        {
            Op = EditOp.Append,
            Content = t.Content,
            SupportCount = t.Support
        }).ToList()
    };
}
