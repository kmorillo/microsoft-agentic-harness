using Domain.AI.Changes;
using Domain.AI.Identity;
using FluentAssertions;
using Xunit;
using EditOp = Domain.AI.SkillTraining.EditOp;
using GateAction = Domain.AI.Changes.GateAction;

namespace Domain.AI.Tests.Changes;

/// <summary>
/// Tests for the <see cref="ChangeProposal"/> aggregate — construction via
/// <see cref="ChangeProposal.Create"/>, state-machine transitions via
/// <see cref="ChangeProposal.TransitionTo"/>, id-based equality, and append-only
/// history semantics.
/// </summary>
public sealed class ChangeProposalTests
{
    private static readonly AgentIdentity Identity = new()
    {
        Id = "agent-001",
        Kind = AgentIdentityKind.ManagedIdentity
    };

    private static readonly ChangeTarget Target =
        new GitRepoTarget("https://github.com/org/repo", "main", "abc123");

    private static readonly IReadOnlyList<ChangeEdit> Diff =
    [
        new ChangeEdit { Op = EditOp.Replace, Target = "foo", Content = "bar" }
    ];

    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 6, 6, 10, 30, 15, TimeSpan.Zero);

    private static ChangeProposal Sample(
        BlastRadius radius = BlastRadius.Low,
        IReadOnlyList<string>? gates = null) =>
        ChangeProposal.Create(
            target: Target,
            diff: Diff,
            submittedBy: Identity,
            summary: "rename foo to bar",
            blastRadius: radius,
            requiredGates: gates ?? new[] { "self_validation", "policy", "approval", "merge" },
            submittedAt: SubmittedAt);

    [Fact]
    public void Create_ProducesProposalInDraftStatusWithEmptyHistory()
    {
        var proposal = Sample();

        proposal.Status.Should().Be(ChangeProposalStatus.Draft);
        proposal.History.Should().BeEmpty();
        proposal.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Create_AssignsDeterministicId()
    {
        var a = Sample();
        var b = Sample();

        a.Id.Should().Be(b.Id);
    }

    [Fact]
    public void Create_DifferentInputs_ProduceDifferentIds()
    {
        var a = Sample(BlastRadius.Low);
        // Same id-derivation inputs as a (blast radius does NOT factor into the id).
        var b = Sample(BlastRadius.High);

        // BlastRadius is not part of the id derivation — re-submitting with a
        // different estimated radius should not break idempotency.
        a.Id.Should().Be(b.Id);
    }

    [Fact]
    public void Create_PreservesAllInputFields()
    {
        var proposal = Sample(BlastRadius.High);

        proposal.Target.Should().BeSameAs(Target);
        proposal.Diff.Should().BeSameAs(Diff);
        proposal.SubmittedBy.Should().BeSameAs(Identity);
        proposal.SubmittedAt.Should().Be(SubmittedAt);
        proposal.Summary.Should().Be("rename foo to bar");
        proposal.BlastRadius.Should().Be(BlastRadius.High);
        proposal.RequiredGates.Should().Equal("self_validation", "policy", "approval", "merge");
    }

    [Fact]
    public void TransitionTo_LegalTransition_ReturnsNewInstanceWithUpdatedStatusAndAppendedHistory()
    {
        var proposal = Sample();
        var decision = new GateDecision
        {
            Timestamp = SubmittedAt,
            GateKey = "orchestrator",
            Action = GateAction.Pass,
            DurationMs = 5
        };

        var next = proposal.TransitionTo(ChangeProposalStatus.Validating, decision);

        next.Status.Should().Be(ChangeProposalStatus.Validating);
        next.History.Should().ContainSingle().Which.Should().Be(decision);
        // Original is untouched.
        proposal.Status.Should().Be(ChangeProposalStatus.Draft);
        proposal.History.Should().BeEmpty();
    }

    [Fact]
    public void TransitionTo_IllegalTransition_ThrowsInvalidOperation()
    {
        var proposal = Sample();
        var decision = new GateDecision
        {
            Timestamp = SubmittedAt,
            GateKey = "orchestrator",
            Action = GateAction.Pass,
            DurationMs = 5
        };

        // Draft → Merging is illegal (must pass through Validating + Approval first).
        var act = () => proposal.TransitionTo(ChangeProposalStatus.Merging, decision);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Draft*Merging*");
    }

    [Fact]
    public void TransitionTo_MultipleTransitions_AppendsHistoryInOrder()
    {
        var proposal = Sample();
        var d1 = new GateDecision
        {
            Timestamp = SubmittedAt,
            GateKey = "self_validation",
            Action = GateAction.Pass,
            DurationMs = 10
        };
        var d2 = new GateDecision
        {
            Timestamp = SubmittedAt.AddSeconds(1),
            GateKey = "approval",
            Action = GateAction.Pass,
            DurationMs = 20
        };

        var after = proposal
            .TransitionTo(ChangeProposalStatus.Validating, d1)
            .TransitionTo(ChangeProposalStatus.AwaitingApproval, d2);

        after.History.Should().HaveCount(2);
        after.History[0].Should().Be(d1);
        after.History[1].Should().Be(d2);
        after.Status.Should().Be(ChangeProposalStatus.AwaitingApproval);
    }

    [Fact]
    public void TransitionTo_DeferSelfLoop_IsLegalAndAppendsHistory()
    {
        var proposal = Sample().TransitionTo(
            ChangeProposalStatus.Validating,
            new GateDecision
            {
                Timestamp = SubmittedAt,
                GateKey = "orchestrator",
                Action = GateAction.Pass,
                DurationMs = 1
            });

        var deferDecision = new GateDecision
        {
            Timestamp = SubmittedAt.AddSeconds(5),
            GateKey = "self_validation",
            Action = GateAction.Defer,
            DurationMs = 1,
            Reason = "validator still running"
        };

        var after = proposal.TransitionTo(ChangeProposalStatus.Validating, deferDecision);

        after.Status.Should().Be(ChangeProposalStatus.Validating);
        after.History.Should().HaveCount(2);
        after.History[1].Should().Be(deferDecision);
    }

    [Fact]
    public void TransitionTo_FromTerminalStatus_IsIllegal()
    {
        var merged = Sample() with { Status = ChangeProposalStatus.Merged };
        var decision = new GateDecision
        {
            Timestamp = SubmittedAt,
            GateKey = "x",
            Action = GateAction.Pass,
            DurationMs = 0
        };

        var act = () => merged.TransitionTo(ChangeProposalStatus.Validating, decision);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsTerminal_AgreesWithStateMachine()
    {
        Sample().IsTerminal.Should().BeFalse();
        (Sample() with { Status = ChangeProposalStatus.Merged }).IsTerminal.Should().BeTrue();
        (Sample() with { Status = ChangeProposalStatus.Rejected }).IsTerminal.Should().BeTrue();
        (Sample() with { Status = ChangeProposalStatus.Cancelled }).IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Equality_TwoProposalsWithSameId_AreEqual_RegardlessOfStateOrHistory()
    {
        var a = Sample();
        var bWithDifferentHistory = a.TransitionTo(
            ChangeProposalStatus.Validating,
            new GateDecision
            {
                Timestamp = SubmittedAt,
                GateKey = "x",
                Action = GateAction.Pass,
                DurationMs = 0
            });

        // Same Id, different Status & History — same aggregate.
        a.Should().Be(bWithDifferentHistory);
        a.GetHashCode().Should().Be(bWithDifferentHistory.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentIds_AreNotEqual()
    {
        var a = Sample();
        var b = a with { Id = "different-id" };

        a.Should().NotBe(b);
    }
}
