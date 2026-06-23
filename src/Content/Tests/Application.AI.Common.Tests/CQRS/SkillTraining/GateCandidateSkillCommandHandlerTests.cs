using Application.AI.Common.CQRS.SkillTraining.GateCandidateSkill;
using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.CQRS.SkillTraining;

public class GateCandidateSkillCommandHandlerTests
{
    private static GateCandidateSkillCommandHandler NewSut()
        => new(new GateEvaluator());

    [Fact]
    public async Task Handle_AcceptNewBest_ReturnsSuccessWithPromotedState()
    {
        var sut = NewSut();
        var cmd = new GateCandidateSkillCommand
        {
            CandidateSkill = "cand", CandidateHard = 0.9, CandidateSoft = 0.0,
            CurrentSkill = "curr", CurrentScore = 0.5,
            BestSkill = "best", BestScore = 0.8, BestStep = 3,
            GlobalStep = 7,
            Metric = GateMetric.Hard
        };

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(GateAction.AcceptNewBest);
        result.Value.BestStep.Should().Be(7);
        result.Value.BestScore.Should().Be(0.9);
    }

    [Fact]
    public async Task Handle_Reject_ReturnsSuccessWithUnchangedState()
    {
        var sut = NewSut();
        var cmd = new GateCandidateSkillCommand
        {
            CandidateSkill = "cand", CandidateHard = 0.3, CandidateSoft = 0.0,
            CurrentSkill = "curr", CurrentScore = 0.5,
            BestSkill = "best", BestScore = 0.8, BestStep = 3,
            GlobalStep = 9,
            Metric = GateMetric.Hard
        };

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(GateAction.Reject);
        result.Value.CurrentSkill.Should().Be("curr");
        result.Value.BestSkill.Should().Be("best");
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        var sut = NewSut();

        var act = () => sut.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_TwoSplitMode_HeldInRegression_Rejects()
    {
        var sut = NewSut();
        var cmd = new GateCandidateSkillCommand
        {
            Mode = GateMode.TwoSplitNonRegression,
            CandidateSkill = "cand", CandidateHard = 0.9, CandidateSoft = 0.0,
            CandidateHeldInHard = 0.4, CandidateHeldInSoft = 0.0,
            CurrentSkill = "curr", CurrentScore = 0.5, CurrentHeldInScore = 0.6,
            BestSkill = "best", BestScore = 0.8, BestStep = 3,
            GlobalStep = 9,
            Metric = GateMetric.Hard
        };

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(
            GateAction.Reject,
            because: "held-out improved (0.5→0.9) but held-in regressed (0.6→0.4)");
        result.Value.CandidateHeldInScore.Should().Be(0.4);
        result.Value.CurrentHeldInScore.Should().Be(0.6);
    }

    [Fact]
    public async Task Handle_TwoSplitMode_BothSplitsImprove_AcceptsNewBest()
    {
        var sut = NewSut();
        var cmd = new GateCandidateSkillCommand
        {
            Mode = GateMode.TwoSplitNonRegression,
            CandidateSkill = "cand", CandidateHard = 0.9, CandidateSoft = 0.0,
            CandidateHeldInHard = 0.7, CandidateHeldInSoft = 0.0,
            CurrentSkill = "curr", CurrentScore = 0.5, CurrentHeldInScore = 0.6,
            BestSkill = "best", BestScore = 0.8, BestStep = 3,
            GlobalStep = 9,
            Metric = GateMetric.Hard
        };

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(GateAction.AcceptNewBest);
        result.Value.BestScore.Should().Be(0.9);
        result.Value.CandidateHeldInScore.Should().Be(0.7);
    }
}
