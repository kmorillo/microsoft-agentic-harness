using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class PatchApplierTests
{
    private static readonly PatchApplier Sut = new();

    [Fact]
    public void Apply_EmptyPatch_ReturnsOriginalContentUnchanged()
    {
        var patch = new Patch { Edits = [] };

        var report = Sut.Apply("# Skill\n\nbody", patch);

        report.NewSkillContent.Should().Be("# Skill\n\nbody");
        report.AppliedEdits.Should().BeEmpty();
        report.FailedEdits.Should().BeEmpty();
        report.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Apply_AppendOp_AppendsContentAtEnd()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Append, Content = "new rule" }]
        };

        var report = Sut.Apply("existing", patch);

        report.NewSkillContent.Should().Be("existing\n\nnew rule");
        report.AppliedEdits.Should().HaveCount(1);
        report.FailedEdits.Should().BeEmpty();
    }

    [Fact]
    public void Apply_AppendOp_OnEmptyDocument_DoesNotPrefixNewlines()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Append, Content = "first" }]
        };

        var report = Sut.Apply(string.Empty, patch);

        report.NewSkillContent.Should().Be("first");
    }

    [Fact]
    public void Apply_InsertAfterOp_InsertsContentVerbatimAfterTarget()
    {
        // Content carries its own leading separator — the applier never injects whitespace.
        // This matches SkillOpt convention where Target/Content already encode terminators.
        var content = "## Approach\n- step one\n\n## Edge Cases\n- nothing yet";
        var patch = new Patch
        {
            Edits =
            [
                new Edit
                {
                    Op = EditOp.InsertAfter,
                    Target = "- step one",
                    Content = "\n- step two"
                }
            ]
        };

        var report = Sut.Apply(content, patch);

        report.NewSkillContent.Should().Be(
            "## Approach\n- step one\n- step two\n\n## Edge Cases\n- nothing yet");
        report.AppliedEdits.Should().HaveCount(1);
    }

    [Fact]
    public void Apply_InsertAfterOp_DoesNotInjectSeparator_WhenContentLacksOne()
    {
        // Optimizer omitted a leading newline — applier must NOT silently add one,
        // because Target may have ended on a token boundary the optimizer cares about.
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.InsertAfter, Target = "abc", Content = "XYZ" }]
        };

        var report = Sut.Apply("abc-tail", patch);

        report.NewSkillContent.Should().Be("abcXYZ-tail");
    }

    [Fact]
    public void Apply_ReplaceOp_ReplacesTarget()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Replace, Target = "Always do X", Content = "Always do Y" }
            ]
        };

        var report = Sut.Apply("Rule: Always do X.", patch);

        report.NewSkillContent.Should().Be("Rule: Always do Y.");
    }

    [Fact]
    public void Apply_DeleteOp_RemovesTarget()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Delete, Target = "- removed rule\n" }]
        };

        var report = Sut.Apply("- kept rule\n- removed rule\n- also kept\n", patch);

        report.NewSkillContent.Should().Be("- kept rule\n- also kept\n");
        report.AppliedEdits.Should().HaveCount(1);
    }

    [Fact]
    public void Apply_TargetNotFound_RecordsFailedEditWithoutMutating()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Replace, Target = "nonexistent", Content = "replacement" }
            ]
        };

        var report = Sut.Apply("original content", patch);

        report.NewSkillContent.Should().Be("original content");
        report.AppliedEdits.Should().BeEmpty();
        report.FailedEdits.Should().HaveCount(1);
        report.FailedEdits[0].Reason.Should().Contain("target not found", because: "callers rely on this string for audit");
    }

    [Theory]
    [InlineData(EditOp.Append)]
    [InlineData(EditOp.InsertAfter)]
    [InlineData(EditOp.Replace)]
    public void Apply_EmptyContent_OnNonDeleteOp_RecordsFailedEdit(EditOp op)
    {
        // An LLM that omits `content` on Replace would otherwise silently turn it into a Delete,
        // corrupting the audit trail. Force the optimizer to use Delete explicitly for removal.
        var edit = new Edit
        {
            Op = op,
            Target = op == EditOp.Append ? string.Empty : "anything",
            Content = string.Empty
        };
        var patch = new Patch { Edits = [edit] };

        var report = Sut.Apply("anything", patch);

        report.NewSkillContent.Should().Be("anything");
        report.AppliedEdits.Should().BeEmpty();
        report.FailedEdits.Should().HaveCount(1);
        report.FailedEdits[0].Reason.Should().Contain("Delete to remove",
            because: "callers and prompts rely on this hint to steer the optimizer");
    }

    [Fact]
    public void Apply_ReplaceWithIdenticalContent_AppliesButReportsNoChanges()
    {
        // Replace "foo" -> "foo" is a content-identity no-op. Edit is recorded as Applied
        // (because the operation completed), but HasChanges must be false so downstream
        // gating/checkpointing doesn't treat it as a real document change.
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Replace, Target = "foo", Content = "foo" }]
        };

        var report = Sut.Apply("foo bar", patch);

        report.NewSkillContent.Should().Be("foo bar");
        report.AppliedEdits.Should().HaveCount(1);
        report.HasChanges.Should().BeFalse(
            because: "HasChanges must reflect content equality, not applied-edit count");
    }

    [Fact]
    public void Apply_DeleteOp_EmptyTarget_RecordsFailedEdit()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Delete, Target = string.Empty }]
        };

        var report = Sut.Apply("anything", patch);

        report.FailedEdits.Should().HaveCount(1);
        report.FailedEdits[0].Reason.Should().Contain("target required");
    }

    [Fact]
    public void Apply_InsertAfterOp_EmptyTarget_RecordsFailedEdit()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.InsertAfter, Target = string.Empty, Content = "x" }]
        };

        var report = Sut.Apply("anything", patch);

        report.FailedEdits.Should().HaveCount(1);
        report.FailedEdits[0].Reason.Should().Contain("target required");
    }

    [Fact]
    public void Apply_MultipleEdits_AppliesInOrder_AndLaterEditsSeeEarlierMutations()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Replace, Target = "alpha", Content = "beta" },
                new Edit { Op = EditOp.Replace, Target = "beta", Content = "gamma" }
            ]
        };

        var report = Sut.Apply("alpha", patch);

        report.NewSkillContent.Should().Be("gamma");
        report.AppliedEdits.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_PartialFailure_AppliesSuccessfulEdits_RecordsFailures()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Replace, Target = "alpha", Content = "beta" },
                new Edit { Op = EditOp.Replace, Target = "missing", Content = "x" },
                new Edit { Op = EditOp.Append, Content = "tail" }
            ]
        };

        var report = Sut.Apply("alpha", patch);

        report.NewSkillContent.Should().Be("beta\n\ntail");
        report.AppliedEdits.Should().HaveCount(2);
        report.FailedEdits.Should().HaveCount(1);
    }

    [Fact]
    public void Apply_FirstOccurrenceOnly_DoesNotReplaceAllInstances()
    {
        var patch = new Patch
        {
            Edits = [new Edit { Op = EditOp.Replace, Target = "x", Content = "y" }]
        };

        var report = Sut.Apply("x and x", patch);

        report.NewSkillContent.Should().Be("y and x", because: "edits target a single location, not all occurrences");
    }

    [Fact]
    public void Apply_PartialFailure_HasChangesTrue_WhenSomeEditsLanded()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Replace, Target = "alpha", Content = "beta" },
                new Edit { Op = EditOp.Replace, Target = "nope", Content = "x" }
            ]
        };

        var report = Sut.Apply("alpha", patch);

        report.HasChanges.Should().BeTrue();
        report.AppliedEdits.Should().HaveCount(1);
        report.FailedEdits.Should().HaveCount(1);
    }

    [Fact]
    public void Apply_AllEditsFail_HasChangesFalse_AndContentUnchanged()
    {
        var patch = new Patch
        {
            Edits =
            [
                new Edit { Op = EditOp.Replace, Target = "missing-a", Content = "x" },
                new Edit { Op = EditOp.Delete, Target = "missing-b" }
            ]
        };

        var report = Sut.Apply("original", patch);

        report.NewSkillContent.Should().Be("original");
        report.HasChanges.Should().BeFalse();
        report.AppliedEdits.Should().BeEmpty();
        report.FailedEdits.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_NullPatch_Throws()
    {
        var act = () => Sut.Apply("any", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Apply_NullCurrentContent_Throws()
    {
        var act = () => Sut.Apply(null!, new Patch());

        act.Should().Throw<ArgumentNullException>();
    }
}
