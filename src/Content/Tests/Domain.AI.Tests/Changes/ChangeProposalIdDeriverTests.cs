using Domain.AI.Changes;
using Domain.AI.Identity;
using FluentAssertions;
using Xunit;
using EditOp = Domain.AI.SkillTraining.EditOp;

namespace Domain.AI.Tests.Changes;

/// <summary>
/// Tests for <see cref="ChangeProposalIdDeriver"/> — verifies the id is stable across
/// equivalent inputs (idempotent re-submission) and changes whenever any input
/// differs (no silent collisions). Without these guarantees the orchestrator's
/// idempotency check is broken.
/// </summary>
public sealed class ChangeProposalIdDeriverTests
{
    private static readonly AgentIdentity Identity = new()
    {
        Id = "agent-001",
        Kind = AgentIdentityKind.ManagedIdentity
    };

    private static readonly ChangeTarget GitTarget =
        new GitRepoTarget("https://github.com/org/repo", "main", "abc123");

    private static readonly IReadOnlyList<ChangeEdit> SampleDiff =
    [
        new ChangeEdit { Op = EditOp.Replace, Target = "foo", Content = "bar" }
    ];

    private static readonly DateTimeOffset SampleTime =
        new(2026, 6, 6, 10, 30, 15, TimeSpan.Zero);

    [Fact]
    public void Derive_SameInputs_ProducesSameId()
    {
        var a = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, SampleTime);
        var b = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, SampleTime);

        a.Should().Be(b);
    }

    [Fact]
    public void Derive_TimesInSameBucket_ProduceSameId()
    {
        var t1 = new DateTimeOffset(2026, 6, 6, 10, 30, 00, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 6, 6, 10, 30, 59, TimeSpan.Zero);

        var a = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, t1);
        var b = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, t2);

        a.Should().Be(b);
    }

    [Fact]
    public void Derive_TimesAcrossBucketBoundary_ProduceDifferentIds()
    {
        var t1 = new DateTimeOffset(2026, 6, 6, 10, 30, 59, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 6, 6, 10, 31, 00, TimeSpan.Zero);

        var a = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, t1);
        var b = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, t2);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Derive_DifferentTarget_ProducesDifferentId()
    {
        var otherTarget = new GitRepoTarget("https://github.com/org/repo", "main", "different-sha");

        var a = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, SampleTime);
        var b = ChangeProposalIdDeriver.Derive(otherTarget, SampleDiff, Identity, SampleTime);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Derive_DifferentTargetType_ProducesDifferentId()
    {
        var k8s = new KubernetesResourceTarget("ctx", "v1", "Pod", "ns", "name");

        var a = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, SampleTime);
        var b = ChangeProposalIdDeriver.Derive(k8s, SampleDiff, Identity, SampleTime);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Derive_DifferentDiffContent_ProducesDifferentId()
    {
        IReadOnlyList<ChangeEdit> diffA =
            [new ChangeEdit { Op = EditOp.Replace, Target = "foo", Content = "bar" }];
        IReadOnlyList<ChangeEdit> diffB =
            [new ChangeEdit { Op = EditOp.Replace, Target = "foo", Content = "baz" }];

        var a = ChangeProposalIdDeriver.Derive(GitTarget, diffA, Identity, SampleTime);
        var b = ChangeProposalIdDeriver.Derive(GitTarget, diffB, Identity, SampleTime);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Derive_DifferentDiffOrder_ProducesDifferentId()
    {
        // The diff is an ordered apply list — reordering changes semantics.
        var e1 = new ChangeEdit { Op = EditOp.Replace, Target = "x", Content = "1" };
        var e2 = new ChangeEdit { Op = EditOp.Replace, Target = "y", Content = "2" };

        var ab = ChangeProposalIdDeriver.Derive(GitTarget, new[] { e1, e2 }, Identity, SampleTime);
        var ba = ChangeProposalIdDeriver.Derive(GitTarget, new[] { e2, e1 }, Identity, SampleTime);

        ab.Should().NotBe(ba);
    }

    [Fact]
    public void Derive_DifferentEditOp_ProducesDifferentId()
    {
        IReadOnlyList<ChangeEdit> diffReplace =
            [new ChangeEdit { Op = EditOp.Replace, Target = "foo", Content = "x" }];
        IReadOnlyList<ChangeEdit> diffDelete =
            [new ChangeEdit { Op = EditOp.Delete, Target = "foo", Content = "x" }];

        var a = ChangeProposalIdDeriver.Derive(GitTarget, diffReplace, Identity, SampleTime);
        var b = ChangeProposalIdDeriver.Derive(GitTarget, diffDelete, Identity, SampleTime);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Derive_DifferentSubmitter_ProducesDifferentId()
    {
        var otherIdentity = new AgentIdentity
        {
            Id = "agent-002",
            Kind = AgentIdentityKind.ManagedIdentity
        };

        var a = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, SampleTime);
        var b = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, otherIdentity, SampleTime);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Derive_LengthPrefixedTargetAndContent_PreventCollisions()
    {
        // If we naively concatenated without length prefixes, these two edits would
        // hash to the same canonical string: "Replace:foobarbaz" vs "Replace:foobar:baz".
        // Length prefixing makes them distinct.
        IReadOnlyList<ChangeEdit> diffA =
            [new ChangeEdit { Op = EditOp.Replace, Target = "foo", Content = "barbaz" }];
        IReadOnlyList<ChangeEdit> diffB =
            [new ChangeEdit { Op = EditOp.Replace, Target = "foobar", Content = "baz" }];

        var a = ChangeProposalIdDeriver.Derive(GitTarget, diffA, Identity, SampleTime);
        var b = ChangeProposalIdDeriver.Derive(GitTarget, diffB, Identity, SampleTime);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Derive_ProducesBase64UrlSafeString()
    {
        var id = ChangeProposalIdDeriver.Derive(GitTarget, SampleDiff, Identity, SampleTime);

        id.Should().HaveLength(43, "SHA-256 → 32 bytes → 43 chars Base64URL (no padding)");
        id.Should().NotContain("+");
        id.Should().NotContain("/");
        id.Should().NotContain("=");
        id.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Canonicalize_IncludesVersionPrefix()
    {
        var canonical = ChangeProposalIdDeriver.Canonicalize(GitTarget, SampleDiff, Identity, SampleTime);

        canonical.Should().StartWith("v1|");
    }

    [Fact]
    public void FloorToBucket_TimesInSameBucket_FloorToSameInstant()
    {
        var t1 = new DateTimeOffset(2026, 6, 6, 10, 30, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 6, 6, 10, 30, 59, TimeSpan.Zero);

        ChangeProposalIdDeriver.FloorToBucket(t1).Should().Be(t1);
        ChangeProposalIdDeriver.FloorToBucket(t2).Should().Be(t1);
    }

    [Fact]
    public void IdBucket_IsOneMinute()
    {
        ChangeProposalIdDeriver.IdBucket.Should().Be(TimeSpan.FromMinutes(1));
    }
}
