using Domain.AI.Changes;
using FluentAssertions;
using Xunit;

namespace Domain.AI.Tests.Changes;

/// <summary>
/// Anchors the numeric values of <see cref="BlastRadius"/>, <see cref="ChangeProposalStatus"/>,
/// <see cref="ChangeTargetKind"/>, and <see cref="GateAction"/>. A change to the wire
/// values would break audit history and persisted state across upgrades, so the test
/// forces a conscious choice if anyone renumbers.
/// </summary>
public sealed class ChangeProposalEnumTests
{
    [Theory]
    [InlineData(BlastRadius.Trivial, 0)]
    [InlineData(BlastRadius.Low, 1)]
    [InlineData(BlastRadius.Medium, 2)]
    [InlineData(BlastRadius.High, 3)]
    [InlineData(BlastRadius.Critical, 4)]
    public void BlastRadius_NumericValues_Match(BlastRadius value, int expected)
    {
        ((int)value).Should().Be(expected);
    }

    [Fact]
    public void BlastRadius_Ordering_IsStrictlyIncreasing()
    {
        ((int)BlastRadius.Trivial).Should().BeLessThan((int)BlastRadius.Low);
        ((int)BlastRadius.Low).Should().BeLessThan((int)BlastRadius.Medium);
        ((int)BlastRadius.Medium).Should().BeLessThan((int)BlastRadius.High);
        ((int)BlastRadius.High).Should().BeLessThan((int)BlastRadius.Critical);
    }

    [Theory]
    [InlineData(ChangeProposalStatus.Draft, 0)]
    [InlineData(ChangeProposalStatus.Validating, 1)]
    [InlineData(ChangeProposalStatus.AwaitingApproval, 2)]
    [InlineData(ChangeProposalStatus.Approved, 3)]
    [InlineData(ChangeProposalStatus.Merging, 4)]
    [InlineData(ChangeProposalStatus.Merged, 5)]
    [InlineData(ChangeProposalStatus.Rejected, 6)]
    [InlineData(ChangeProposalStatus.Cancelled, 7)]
    public void ChangeProposalStatus_NumericValues_Match(ChangeProposalStatus value, int expected)
    {
        ((int)value).Should().Be(expected);
    }

    [Theory]
    [InlineData(ChangeTargetKind.Unspecified, 0)]
    [InlineData(ChangeTargetKind.GitRepo, 1)]
    [InlineData(ChangeTargetKind.KubernetesResource, 2)]
    [InlineData(ChangeTargetKind.IacDeployment, 3)]
    public void ChangeTargetKind_NumericValues_Match(ChangeTargetKind value, int expected)
    {
        ((int)value).Should().Be(expected);
    }

    [Fact]
    public void GateAction_HasExactlyThreeMembers_PassFailDefer()
    {
        var members = Enum.GetValues<GateAction>();

        members.Should().BeEquivalentTo(new[]
        {
            GateAction.Pass,
            GateAction.Fail,
            GateAction.Defer
        });
    }
}
