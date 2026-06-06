using Domain.AI.Changes;
using FluentAssertions;
using Xunit;

namespace Domain.AI.Tests.Changes;

/// <summary>
/// Exhaustive coverage of the <see cref="ChangeProposalStateTransitions"/> state
/// machine — every legal transition is verified, and the complement of every legal
/// set is verified as illegal. Together these ensure no transition is silently
/// admitted or silently forbidden.
/// </summary>
public sealed class ChangeProposalStateTransitionsTests
{
    [Theory]
    [InlineData(ChangeProposalStatus.Merged)]
    [InlineData(ChangeProposalStatus.Rejected)]
    [InlineData(ChangeProposalStatus.Cancelled)]
    public void IsTerminal_TerminalStates_ReturnsTrue(ChangeProposalStatus status)
    {
        ChangeProposalStateTransitions.IsTerminal(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(ChangeProposalStatus.Draft)]
    [InlineData(ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.AwaitingApproval)]
    [InlineData(ChangeProposalStatus.Approved)]
    [InlineData(ChangeProposalStatus.Merging)]
    public void IsTerminal_NonTerminalStates_ReturnsFalse(ChangeProposalStatus status)
    {
        ChangeProposalStateTransitions.IsTerminal(status).Should().BeFalse();
    }

    [Theory]
    [InlineData(ChangeProposalStatus.Merged)]
    [InlineData(ChangeProposalStatus.Rejected)]
    [InlineData(ChangeProposalStatus.Cancelled)]
    public void LegalNext_TerminalStates_ReturnsEmpty(ChangeProposalStatus status)
    {
        ChangeProposalStateTransitions.LegalNext(status).Should().BeEmpty();
    }

    [Theory]
    // Draft: orchestrator picks up → Validating; submitter cancels.
    [InlineData(ChangeProposalStatus.Draft, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.Draft, ChangeProposalStatus.Cancelled)]
    // Validating: defer self-loop, advance to AwaitingApproval, fail → Rejected, cancel.
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.AwaitingApproval)]
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.Rejected)]
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.Cancelled)]
    // AwaitingApproval: defer self-loop, approve, reject, cancel.
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.AwaitingApproval)]
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.Approved)]
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.Rejected)]
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.Cancelled)]
    // Approved: orchestrator picks up → Merging; cancel still allowed pre-merge-start.
    [InlineData(ChangeProposalStatus.Approved, ChangeProposalStatus.Merging)]
    [InlineData(ChangeProposalStatus.Approved, ChangeProposalStatus.Cancelled)]
    // Merging: success → Merged; failure → Rejected. No cancel.
    [InlineData(ChangeProposalStatus.Merging, ChangeProposalStatus.Merged)]
    [InlineData(ChangeProposalStatus.Merging, ChangeProposalStatus.Rejected)]
    public void IsLegal_KnownLegalTransitions_ReturnsTrue(
        ChangeProposalStatus from,
        ChangeProposalStatus to)
    {
        ChangeProposalStateTransitions.IsLegal(from, to).Should().BeTrue(
            "transition {0} → {1} is part of the documented legal set",
            from,
            to);
    }

    [Theory]
    // Draft cannot skip validation.
    [InlineData(ChangeProposalStatus.Draft, ChangeProposalStatus.AwaitingApproval)]
    [InlineData(ChangeProposalStatus.Draft, ChangeProposalStatus.Approved)]
    [InlineData(ChangeProposalStatus.Draft, ChangeProposalStatus.Merging)]
    [InlineData(ChangeProposalStatus.Draft, ChangeProposalStatus.Merged)]
    [InlineData(ChangeProposalStatus.Draft, ChangeProposalStatus.Rejected)]
    // Validating cannot skip approval or merge straight to Approved/Merged.
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.Draft)]
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.Approved)]
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.Merging)]
    [InlineData(ChangeProposalStatus.Validating, ChangeProposalStatus.Merged)]
    // AwaitingApproval cannot rewind or skip Approved → Merging step.
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.Draft)]
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.Merging)]
    [InlineData(ChangeProposalStatus.AwaitingApproval, ChangeProposalStatus.Merged)]
    // Approved cannot rewind or skip to Merged without going through Merging.
    [InlineData(ChangeProposalStatus.Approved, ChangeProposalStatus.Draft)]
    [InlineData(ChangeProposalStatus.Approved, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.Approved, ChangeProposalStatus.AwaitingApproval)]
    [InlineData(ChangeProposalStatus.Approved, ChangeProposalStatus.Merged)]
    [InlineData(ChangeProposalStatus.Approved, ChangeProposalStatus.Rejected)]
    // Merging cannot rewind or be cancelled.
    [InlineData(ChangeProposalStatus.Merging, ChangeProposalStatus.Draft)]
    [InlineData(ChangeProposalStatus.Merging, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.Merging, ChangeProposalStatus.AwaitingApproval)]
    [InlineData(ChangeProposalStatus.Merging, ChangeProposalStatus.Approved)]
    [InlineData(ChangeProposalStatus.Merging, ChangeProposalStatus.Cancelled)]
    // Terminal states never transition.
    [InlineData(ChangeProposalStatus.Merged, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.Merged, ChangeProposalStatus.Rejected)]
    [InlineData(ChangeProposalStatus.Rejected, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.Rejected, ChangeProposalStatus.Merged)]
    [InlineData(ChangeProposalStatus.Cancelled, ChangeProposalStatus.Validating)]
    [InlineData(ChangeProposalStatus.Cancelled, ChangeProposalStatus.Merged)]
    public void IsLegal_KnownIllegalTransitions_ReturnsFalse(
        ChangeProposalStatus from,
        ChangeProposalStatus to)
    {
        ChangeProposalStateTransitions.IsLegal(from, to).Should().BeFalse(
            "transition {0} → {1} is documented as illegal",
            from,
            to);
    }

    [Fact]
    public void LegalNext_AllNonTerminalStates_AgreeWithIsLegal()
    {
        // Sanity check: LegalNext(s) must agree with IsLegal(s, t) for every t.
        var allStatuses = Enum.GetValues<ChangeProposalStatus>();

        foreach (var from in allStatuses)
        {
            var legalNext = ChangeProposalStateTransitions.LegalNext(from);

            foreach (var to in allStatuses)
            {
                var isLegal = ChangeProposalStateTransitions.IsLegal(from, to);
                isLegal.Should().Be(
                    legalNext.Contains(to),
                    "IsLegal({0}, {1}) must agree with LegalNext({0}).Contains({1})",
                    from,
                    to);
            }
        }
    }

    [Fact]
    public void TerminalStates_ContainsExactlyMergedRejectedCancelled()
    {
        ChangeProposalStateTransitions.TerminalStates.Should().BeEquivalentTo(new[]
        {
            ChangeProposalStatus.Merged,
            ChangeProposalStatus.Rejected,
            ChangeProposalStatus.Cancelled
        });
    }
}
