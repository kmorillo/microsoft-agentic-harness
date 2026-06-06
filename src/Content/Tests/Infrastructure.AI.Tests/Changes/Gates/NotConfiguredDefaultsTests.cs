using Application.AI.Common.Interfaces.Changes;
using Domain.AI.Changes;
using FluentAssertions;
using Infrastructure.AI.Changes.Gates;
using Infrastructure.AI.Tests.Changes.Support;
using Xunit;

namespace Infrastructure.AI.Tests.Changes.Gates;

/// <summary>
/// Tests for the fail-loud placeholder validators and policies. These exist
/// only to scream when DI is incomplete — confirming they throw the right
/// message is the whole behavior.
/// </summary>
public sealed class NotConfiguredDefaultsTests
{
    private static GateContext Ctx() => new()
    {
        Mode = OrchestratorMode.Live,
        AttemptCount = 1,
        EvaluatedAt = TestProposals.DefaultTime,
        CorrelationId = "corr-1"
    };

    [Fact]
    public async Task NotConfiguredValidator_ThrowsWithDirectiveMessage()
    {
        var sut = new NotConfiguredValidator(ChangeTargetKind.GitRepo);

        var act = async () => await sut.ValidateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("No IChangeProposalValidator is registered for target kind 'GitRepo'");
        ex.Which.Message.Should().Contain("services.AddKeyedSingleton<IChangeProposalValidator>(ChangeTargetKind.GitRepo");
    }

    [Fact]
    public void NotConfiguredValidator_KeyIncludesTargetKind()
    {
        new NotConfiguredValidator(ChangeTargetKind.IacDeployment).Key
            .Should().Be("not_configured.IacDeployment");
    }

    [Fact]
    public async Task NotConfiguredPolicy_ThrowsWithDirectiveMessage()
    {
        var sut = new NotConfiguredPolicy();

        var act = async () => await sut.EvaluateAsync(TestProposals.NewProposal(), Ctx(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("No IChangeProposalPolicy is registered");
        ex.Which.Message.Should().Contain("services.AddSingleton<IChangeProposalPolicy");
    }
}
