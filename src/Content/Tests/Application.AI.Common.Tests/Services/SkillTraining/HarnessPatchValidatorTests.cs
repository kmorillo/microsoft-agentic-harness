using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class HarnessPatchValidatorTests
{
    private static readonly HarnessPatchValidator Sut = new(new EditableSurfaceRegistry());

    [Fact]
    public void Validate_EmptyPatch_IsAllowed()
    {
        Sut.Validate(new Patch()).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Validate_AllSkillDocumentEdits_IsAllowed()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Append, Content = "- a" },                                  // defaults to SkillDocument
                new Edit { Op = EditOp.Append, Content = "- b", Surface = HarnessSurface.SkillDocument }
            ]
        };

        var result = Sut.Validate(patch);

        result.IsAllowed.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SingleFrozenSurfaceEdit_IsRejected_WithViolationDetail()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Append, Content = "x", Surface = HarnessSurface.ToolAvailability }]
        };

        var result = Sut.Validate(patch);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().ContainSingle();
        result.Violations[0].EditIndex.Should().Be(0);
        result.Violations[0].Surface.Should().Be(HarnessSurface.ToolAvailability);
        result.Violations[0].FrozenByConstruction.Should().BeFalse(
            because: "tool availability is frozen by policy today, not by construction");
    }

    [Fact]
    public void Validate_FrozenByConstructionEdit_FlagsByConstruction()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Append, Content = "x", Surface = HarnessSurface.DeniedTools }]
        };

        var result = Sut.Validate(patch);

        result.IsAllowed.Should().BeFalse();
        result.Violations[0].FrozenByConstruction.Should().BeTrue();
    }

    [Fact]
    public void Validate_MixedEdits_ReportsOnlyTheFrozenOne_AtCorrectIndex()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Append, Content = "ok" },                                       // index 0 — SkillDocument
                new Edit { Op = EditOp.Append, Content = "bad", Surface = HarnessSurface.SystemPrompt } // index 1 — frozen
            ]
        };

        var result = Sut.Validate(patch);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().ContainSingle();
        result.Violations[0].EditIndex.Should().Be(1);
        result.Violations[0].Surface.Should().Be(HarnessSurface.SystemPrompt);
    }

    [Fact]
    public void Validate_MultipleFrozenEdits_ReportsAllViolations()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Append, Content = "a", Surface = HarnessSurface.SystemPrompt },
                new Edit { Op = EditOp.Append, Content = "b" },                                         // SkillDocument — allowed
                new Edit { Op = EditOp.Append, Content = "c", Surface = HarnessSurface.AutonomyTier }
            ]
        };

        var result = Sut.Validate(patch);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().HaveCount(2);
        result.Violations.Select(v => v.EditIndex).Should().Equal(0, 2);
    }

    [Theory]
    [InlineData(EditOp.Append)]
    [InlineData(EditOp.InsertAfter)]
    [InlineData(EditOp.Replace)]
    [InlineData(EditOp.Delete)]
    public void Validate_IsOpAgnostic_RejectsFrozenSurfaceRegardlessOfOp(EditOp op)
    {
        // The fence gates on the target surface, not the edit operation: a frozen surface is off-limits
        // whether the loop wants to Append, InsertAfter, Replace, or Delete on it.
        var patch = new Patch
        {
            Edits = [new Edit { Op = op, Target = "x", Content = "y", Surface = HarnessSurface.ContentSafetyConfig }]
        };

        var result = Sut.Validate(patch);

        result.IsAllowed.Should().BeFalse();
        result.Violations[0].Surface.Should().Be(HarnessSurface.ContentSafetyConfig);
        result.Violations[0].FrozenByConstruction.Should().BeTrue();
    }

    [Fact]
    public void Validate_NullPatch_Throws()
    {
        var act = () => Sut.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullRegistry_Throws()
    {
        var act = () => new HarnessPatchValidator(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
