using Domain.AI.Changes;
using FluentAssertions;
using Xunit;

namespace Domain.AI.Tests.Changes;

/// <summary>
/// Tests for <see cref="GateDecision"/> history-entry record. Domain validation is
/// intentionally absent — the orchestrator constructs these and never accepts them
/// from a system boundary, so invariants are enforced at the construction site.
/// </summary>
public sealed class GateDecisionTests
{
    [Fact]
    public void Constructor_WithRequiredProperties_SetsValues()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-06T10:00:00Z");
        var decision = new GateDecision
        {
            Timestamp = timestamp,
            GateKey = "self_validation",
            Action = GateAction.Pass,
            DurationMs = 1234
        };

        decision.Timestamp.Should().Be(timestamp);
        decision.GateKey.Should().Be("self_validation");
        decision.Action.Should().Be(GateAction.Pass);
        decision.DurationMs.Should().Be(1234);
    }

    [Fact]
    public void Defaults_OptionalProperties_HaveExpectedDefaults()
    {
        var decision = new GateDecision
        {
            Timestamp = DateTimeOffset.UtcNow,
            GateKey = "policy",
            Action = GateAction.Pass,
            DurationMs = 0
        };

        decision.Reason.Should().BeEmpty();
        decision.EvidenceHash.Should().BeNull();
        decision.ReviewerId.Should().BeNull();
    }

    [Fact]
    public void ApprovalGate_DecisionWithReviewerId_CapturesReviewer()
    {
        var decision = new GateDecision
        {
            Timestamp = DateTimeOffset.UtcNow,
            GateKey = "approval",
            Action = GateAction.Pass,
            DurationMs = 60_000,
            ReviewerId = "user-42",
            Reason = "approved via portal"
        };

        decision.ReviewerId.Should().Be("user-42");
    }

    [Fact]
    public void Records_ValueEquality_Holds()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new GateDecision
        {
            Timestamp = ts,
            GateKey = "policy",
            Action = GateAction.Fail,
            DurationMs = 100,
            Reason = "missing tag",
            EvidenceHash = "sha256:abc"
        };

        var b = new GateDecision
        {
            Timestamp = ts,
            GateKey = "policy",
            Action = GateAction.Fail,
            DurationMs = 100,
            Reason = "missing tag",
            EvidenceHash = "sha256:abc"
        };

        a.Should().Be(b);
    }

    [Fact]
    public void With_Expression_ProducesNewInstance_OriginalUnchanged()
    {
        var original = new GateDecision
        {
            Timestamp = DateTimeOffset.UtcNow,
            GateKey = "approval",
            Action = GateAction.Defer,
            DurationMs = 50,
            Reason = "awaiting reviewer"
        };

        var updated = original with { Action = GateAction.Pass, Reason = "approved" };

        original.Action.Should().Be(GateAction.Defer);
        original.Reason.Should().Be("awaiting reviewer");
        updated.Action.Should().Be(GateAction.Pass);
        updated.Reason.Should().Be("approved");
    }
}
