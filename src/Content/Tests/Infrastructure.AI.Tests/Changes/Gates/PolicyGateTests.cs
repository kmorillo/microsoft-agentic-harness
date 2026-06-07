using Application.AI.Common.Interfaces.Changes;
using Domain.AI.Changes;
using FluentAssertions;
using Infrastructure.AI.Changes.Gates;
using Infrastructure.AI.Tests.Changes.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GateAction = Domain.AI.Changes.GateAction;

namespace Infrastructure.AI.Tests.Changes.Gates;

public sealed class PolicyGateTests
{
    private sealed class ScriptedPolicy(string key, params PolicyFinding[] findings) : IChangeProposalPolicy
    {
        public string Key { get; } = key;
        public Task<IReadOnlyList<PolicyFinding>> EvaluateAsync(ChangeProposal proposal, GateContext context, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PolicyFinding>>(findings);
    }

    private sealed class ThrowingPolicy(string key) : IChangeProposalPolicy
    {
        public string Key { get; } = key;
        public Task<IReadOnlyList<PolicyFinding>> EvaluateAsync(ChangeProposal proposal, GateContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("policy exploded");
    }

    private static PolicyFinding Finding(string policyKey, PolicyFindingSeverity sev, string msg = "issue") => new()
    {
        PolicyKey = policyKey,
        Severity = sev,
        Message = msg
    };

    private static (PolicyGate Gate, string TempDir) Build(params IChangeProposalPolicy[] policies)
    {
        var (monitor, dir) = TestConfig.NewMonitor();
        return (new PolicyGate(policies, monitor, NullLogger<PolicyGate>.Instance), dir);
    }

    private static GateContext Ctx() => new()
    {
        Mode = OrchestratorMode.Live,
        AttemptCount = 1,
        EvaluatedAt = TestProposals.DefaultTime,
        CorrelationId = "corr-1"
    };

    [Fact]
    public async Task EvaluateAsync_NoPoliciesRegistered_FailsWithDirectiveMessage()
    {
        var (sut, dir) = Build();
        try
        {
            var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

            result.Action.Should().Be(GateAction.Fail);
            result.Reason.Should().Contain("No IChangeProposalPolicy is registered");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EvaluateAsync_NoFindings_ReturnsPass()
    {
        var (sut, dir) = Build(new ScriptedPolicy("checkov"));
        try
        {
            var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

            result.Action.Should().Be(GateAction.Pass);
            result.Reason.Should().Contain("no findings");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EvaluateAsync_FindingsBelowThreshold_ReturnsPass()
    {
        // Default blocking threshold is High; Low + Medium should not block.
        var (sut, dir) = Build(
            new ScriptedPolicy("checkov",
                Finding("checkov", PolicyFindingSeverity.Low, "minor"),
                Finding("checkov", PolicyFindingSeverity.Medium, "moderate")));
        try
        {
            var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

            result.Action.Should().Be(GateAction.Pass);
            result.Reason.Should().Contain("below");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EvaluateAsync_HighSeverityFinding_FailsAtDefaultThreshold()
    {
        var (sut, dir) = Build(new ScriptedPolicy("checkov", Finding("checkov", PolicyFindingSeverity.High, "public S3")));
        try
        {
            var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

            result.Action.Should().Be(GateAction.Fail);
            result.Reason.Should().Contain("public S3");
            result.Reason.Should().Contain("checkov");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EvaluateAsync_CriticalSeverityFinding_AlwaysFails()
    {
        var (sut, dir) = Build(new ScriptedPolicy("opa", Finding("opa", PolicyFindingSeverity.Critical, "missing tag")));
        try
        {
            var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

            result.Action.Should().Be(GateAction.Fail);
            result.Reason.Should().Contain("Critical");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EvaluateAsync_AggregatesFindingsAcrossPolicies()
    {
        var (sut, dir) = Build(
            new ScriptedPolicy("checkov", Finding("checkov", PolicyFindingSeverity.Low, "x")),
            new ScriptedPolicy("opa", Finding("opa", PolicyFindingSeverity.High, "y")));
        try
        {
            var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

            result.Action.Should().Be(GateAction.Fail);
            // High from opa drives the block; reason mentions opa.
            result.Reason.Should().Contain("opa");
            result.Reason.Should().Contain("y");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EvaluateAsync_PolicyThrows_ReturnsFailNotThrow()
    {
        var (sut, dir) = Build(new ThrowingPolicy("checkov"));
        try
        {
            var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

            result.Action.Should().Be(GateAction.Fail);
            result.Reason.Should().Contain("InvalidOperationException");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
