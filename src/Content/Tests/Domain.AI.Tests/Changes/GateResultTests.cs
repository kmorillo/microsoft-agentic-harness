using Domain.AI.Changes;
using FluentAssertions;
using Xunit;

namespace Domain.AI.Tests.Changes;

/// <summary>
/// Tests for <see cref="GateResult"/> — verifies each factory enforces its variant's
/// invariants (Fail/Defer require reason; Defer requires positive RetryAfter) and that
/// the resulting record exposes the expected shape.
/// </summary>
public sealed class GateResultTests
{
    [Fact]
    public void Pass_WithNoArguments_ProducesPassWithEmptyReasonAndNoEvidence()
    {
        var result = GateResult.Pass();

        result.Action.Should().Be(GateAction.Pass);
        result.Reason.Should().BeEmpty();
        result.EvidenceHash.Should().BeNull();
        result.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void Pass_WithReasonAndEvidence_PreservesBoth()
    {
        var result = GateResult.Pass("all checks green", "sha256:abc");

        result.Action.Should().Be(GateAction.Pass);
        result.Reason.Should().Be("all checks green");
        result.EvidenceHash.Should().Be("sha256:abc");
        result.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void Fail_WithReason_ProducesFailWithThatReason()
    {
        var result = GateResult.Fail("policy violation: missing tag");

        result.Action.Should().Be(GateAction.Fail);
        result.Reason.Should().Be("policy violation: missing tag");
        result.EvidenceHash.Should().BeNull();
        result.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void Fail_WithEvidence_PreservesEvidenceHash()
    {
        var result = GateResult.Fail("validator failed", "sha256:def");

        result.EvidenceHash.Should().Be("sha256:def");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_WithBlankReason_Throws(string? reason)
    {
        var act = () => GateResult.Fail(reason!);

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Defer_WithReasonAndPositiveRetry_ProducesDefer()
    {
        var retry = TimeSpan.FromSeconds(30);

        var result = GateResult.Defer("awaiting external reviewer", retry);

        result.Action.Should().Be(GateAction.Defer);
        result.Reason.Should().Be("awaiting external reviewer");
        result.RetryAfter.Should().Be(retry);
        result.EvidenceHash.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Defer_WithBlankReason_Throws(string? reason)
    {
        var act = () => GateResult.Defer(reason!, TimeSpan.FromSeconds(10));

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Defer_WithZeroRetry_Throws()
    {
        var act = () => GateResult.Defer("waiting", TimeSpan.Zero);

        act.Should().Throw<ArgumentException>().WithParameterName("retryAfter");
    }

    [Fact]
    public void Defer_WithNegativeRetry_Throws()
    {
        var act = () => GateResult.Defer("waiting", TimeSpan.FromSeconds(-1));

        act.Should().Throw<ArgumentException>().WithParameterName("retryAfter");
    }

    [Fact]
    public void Records_ValueEquality_Holds()
    {
        var a = GateResult.Fail("missing tag", "sha256:xyz");
        var b = GateResult.Fail("missing tag", "sha256:xyz");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
