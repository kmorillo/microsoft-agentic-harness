using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class GateEvaluatorTests
{
    private static readonly GateEvaluator Sut = new();

    // ── SelectGateScore ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.8, 0.6, 0.8)]
    [InlineData(0.0, 0.9, 0.0)]
    public void SelectGateScore_Hard_ReturnsHard(double hard, double soft, double expected)
    {
        Sut.SelectGateScore(hard, soft, GateMetric.Hard).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.2, 0.7, 0.7)]
    [InlineData(0.9, 0.1, 0.1)]
    public void SelectGateScore_Soft_ReturnsSoft(double hard, double soft, double expected)
    {
        Sut.SelectGateScore(hard, soft, GateMetric.Soft).Should().Be(expected);
    }

    [Fact]
    public void SelectGateScore_Mixed_AveragesAtDefaultWeight()
    {
        // (1 - 0.5) * 0.8 + 0.5 * 0.6 = 0.4 + 0.3 = 0.7
        Sut.SelectGateScore(0.8, 0.6, GateMetric.Mixed).Should().BeApproximately(0.7, 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.8)]  // pure hard
    [InlineData(1.0, 0.6)]  // pure soft
    [InlineData(0.25, 0.75)] // (0.75 * 0.8 + 0.25 * 0.6) = 0.6 + 0.15 = 0.75
    public void SelectGateScore_Mixed_AppliesWeight(double weight, double expected)
    {
        Sut.SelectGateScore(0.8, 0.6, GateMetric.Mixed, weight)
            .Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void SelectGateScore_Mixed_OutOfRangeOrNaNWeight_Throws(double weight)
    {
        var act = () => Sut.SelectGateScore(0.8, 0.6, GateMetric.Mixed, weight);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.5, double.NaN)]
    [InlineData(double.PositiveInfinity, 0.5)]
    public void SelectGateScore_NonFiniteScore_Throws(double hard, double soft)
    {
        var act = () => Sut.SelectGateScore(hard, soft, GateMetric.Hard);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Evaluate_NaNCurrentOrBestScore_Throws()
    {
        var act = () => Sut.Evaluate(
            candidateSkill: "c", candidateHard: 0.5, candidateSoft: 0.0,
            currentSkill: "cur", currentScore: double.NaN,
            bestSkill: "best", bestScore: 0.5, bestStep: 0,
            globalStep: 1, metric: GateMetric.Hard);

        act.Should().Throw<ArgumentException>();
    }

    // ── Evaluate ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CandidateStrictlyBeatsBest_PromotesToNewBest_WithGlobalStep()
    {
        var result = Sut.Evaluate(
            candidateSkill: "cand", candidateHard: 0.9, candidateSoft: 0.0,
            currentSkill: "curr", currentScore: 0.5,
            bestSkill: "best", bestScore: 0.8, bestStep: 3,
            globalStep: 7,
            metric: GateMetric.Hard);

        result.Action.Should().Be(GateAction.AcceptNewBest);
        result.CurrentSkill.Should().Be("cand");
        result.CurrentScore.Should().Be(0.9);
        result.BestSkill.Should().Be("cand");
        result.BestScore.Should().Be(0.9);
        result.BestStep.Should().Be(7);
        result.CandidateSkill.Should().Be("cand");
        result.CandidateScore.Should().Be(0.9);
    }

    [Fact]
    public void Evaluate_CandidateBeatsCurrentButTiesBest_AcceptsWithoutPromotion()
    {
        var result = Sut.Evaluate(
            candidateSkill: "cand", candidateHard: 0.8, candidateSoft: 0.0,
            currentSkill: "curr", currentScore: 0.5,
            bestSkill: "best", bestScore: 0.8, bestStep: 3,
            globalStep: 9,
            metric: GateMetric.Hard);

        result.Action.Should().Be(GateAction.Accept);
        result.CurrentSkill.Should().Be("cand");
        result.CurrentScore.Should().Be(0.8);
        result.BestSkill.Should().Be("best", because: "tie with best does not promote");
        result.BestScore.Should().Be(0.8);
        result.BestStep.Should().Be(3, because: "best_step only updates on a new best");
    }

    [Fact]
    public void Evaluate_CandidateTiesCurrent_Rejects()
    {
        var result = Sut.Evaluate(
            candidateSkill: "cand", candidateHard: 0.5, candidateSoft: 0.0,
            currentSkill: "curr", currentScore: 0.5,
            bestSkill: "best", bestScore: 0.8, bestStep: 3,
            globalStep: 9,
            metric: GateMetric.Hard);

        result.Action.Should().Be(GateAction.Reject);
        result.CurrentSkill.Should().Be("curr", because: "reject preserves prior state");
        result.CurrentScore.Should().Be(0.5);
        result.BestSkill.Should().Be("best");
        result.BestScore.Should().Be(0.8);
        result.BestStep.Should().Be(3);
        result.CandidateScore.Should().Be(0.5, because: "candidate score is still recorded for audit");
    }

    [Fact]
    public void Evaluate_CandidateWorseThanCurrent_Rejects()
    {
        var result = Sut.Evaluate(
            candidateSkill: "cand", candidateHard: 0.3, candidateSoft: 0.0,
            currentSkill: "curr", currentScore: 0.5,
            bestSkill: "best", bestScore: 0.8, bestStep: 3,
            globalStep: 9,
            metric: GateMetric.Hard);

        result.Action.Should().Be(GateAction.Reject);
    }

    [Fact]
    public void Evaluate_UsesSoftScore_WhenMetricIsSoft()
    {
        // hard=0.3 (worse than current=0.5) but soft=0.9 should drive Accept under Soft metric
        var result = Sut.Evaluate(
            candidateSkill: "cand", candidateHard: 0.3, candidateSoft: 0.9,
            currentSkill: "curr", currentScore: 0.5,
            bestSkill: "best", bestScore: 0.8, bestStep: 3,
            globalStep: 9,
            metric: GateMetric.Soft);

        result.Action.Should().Be(GateAction.AcceptNewBest);
        result.CandidateScore.Should().Be(0.9);
    }

    [Fact]
    public void Evaluate_UsesMixedProjection_WhenMetricIsMixed()
    {
        // hard=0.4, soft=0.8, weight=0.5 → projected = 0.6
        // current=0.5, best=0.55 → 0.6 > both → AcceptNewBest
        var result = Sut.Evaluate(
            candidateSkill: "cand", candidateHard: 0.4, candidateSoft: 0.8,
            currentSkill: "curr", currentScore: 0.5,
            bestSkill: "best", bestScore: 0.55, bestStep: 3,
            globalStep: 9,
            metric: GateMetric.Mixed,
            mixedWeight: 0.5);

        result.Action.Should().Be(GateAction.AcceptNewBest);
        result.CandidateScore.Should().BeApproximately(0.6, 1e-9);
        result.BestStep.Should().Be(9);
    }

    [Fact]
    public void Evaluate_NullArgs_Throws()
    {
        var act = () => Sut.Evaluate(null!, 0, 0, "", 0, "", 0, 0, 0, GateMetric.Hard);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelectGateScore_UnknownMetric_Throws()
    {
        var act = () => Sut.SelectGateScore(0, 0, (GateMetric)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
