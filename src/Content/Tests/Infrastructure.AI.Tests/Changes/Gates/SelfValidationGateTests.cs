using Application.AI.Common.Interfaces.Changes;
using Domain.AI.Changes;
using FluentAssertions;
using Infrastructure.AI.Changes.Gates;
using Infrastructure.AI.Tests.Changes.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GateAction = Domain.AI.Changes.GateAction;

namespace Infrastructure.AI.Tests.Changes.Gates;

public sealed class SelfValidationGateTests
{
    private sealed class ScriptedValidator(string key, GateResult result) : IChangeProposalValidator
    {
        public string Key { get; } = key;
        public Task<GateResult> ValidateAsync(ChangeProposal proposal, GateContext context, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }

    private sealed class ThrowingValidator(string key) : IChangeProposalValidator
    {
        public string Key { get; } = key;
        public Task<GateResult> ValidateAsync(ChangeProposal proposal, GateContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("validator exploded");
    }

    private static SelfValidationGate Build(params (IChangeProposalValidator Validator, ChangeTargetKind Key)[] validators)
    {
        var services = new ServiceCollection();
        foreach (var (v, k) in validators)
        {
            services.AddKeyedSingleton(k, v);
        }
        return new SelfValidationGate(services.BuildServiceProvider(), NullLogger<SelfValidationGate>.Instance);
    }

    private static GateContext Ctx() => new()
    {
        Mode = OrchestratorMode.Live,
        AttemptCount = 1,
        EvaluatedAt = TestProposals.DefaultTime,
        CorrelationId = "corr-1"
    };

    [Fact]
    public async Task EvaluateAsync_NoValidatorsRegistered_FailsWithDirectiveMessage()
    {
        var sut = Build();
        var proposal = TestProposals.NewProposal();

        var result = await sut.EvaluateAsync(proposal, Ctx(), CancellationToken.None);

        result.Action.Should().Be(GateAction.Fail);
        result.Reason.Should().Contain("No IChangeProposalValidator registered");
        result.Reason.Should().Contain("GitRepo");
    }

    [Fact]
    public async Task EvaluateAsync_AllPass_ReturnsPass()
    {
        var sut = Build(
            (new ScriptedValidator("run_tests", GateResult.Pass("12 tests passed")), ChangeTargetKind.GitRepo),
            (new ScriptedValidator("run_lint", GateResult.Pass("no lint issues")), ChangeTargetKind.GitRepo));

        var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

        result.Action.Should().Be(GateAction.Pass);
        result.Reason.Should().Contain("run_tests");
        result.Reason.Should().Contain("run_lint");
    }

    [Fact]
    public async Task EvaluateAsync_AnyValidatorFails_ShortCircuitsToFail()
    {
        var laterValidator = new ScriptedValidator("run_lint", GateResult.Pass());
        var sut = Build(
            (new ScriptedValidator("run_tests", GateResult.Fail("2 tests broken")), ChangeTargetKind.GitRepo),
            (laterValidator, ChangeTargetKind.GitRepo));

        var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

        result.Action.Should().Be(GateAction.Fail);
        result.Reason.Should().Contain("run_tests");
        result.Reason.Should().Contain("2 tests broken");
    }

    [Fact]
    public async Task EvaluateAsync_ValidatorThrows_ReturnsFailNotThrow()
    {
        var sut = Build((new ThrowingValidator("run_tests"), ChangeTargetKind.GitRepo));

        var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

        result.Action.Should().Be(GateAction.Fail);
        result.Reason.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task EvaluateAsync_AnyValidatorDefers_AggregateDeferWithLongestRetry()
    {
        var sut = Build(
            (new ScriptedValidator("run_tests", GateResult.Pass()), ChangeTargetKind.GitRepo),
            (new ScriptedValidator("run_lint", GateResult.Defer("lint server warming up", TimeSpan.FromSeconds(45))), ChangeTargetKind.GitRepo));

        var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

        result.Action.Should().Be(GateAction.Defer);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(45));
        result.Reason.Should().Contain("run_lint");
    }

    [Fact]
    public async Task EvaluateAsync_RegistrationKeyedByDifferentTargetKind_NotResolved()
    {
        // Validator registered for KubernetesResource won't run against a GitRepo proposal.
        var sut = Build((new ScriptedValidator("kubectl_dry_run", GateResult.Pass()), ChangeTargetKind.KubernetesResource));

        var result = await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

        result.Action.Should().Be(GateAction.Fail);
        result.Reason.Should().Contain("GitRepo");
    }
}
