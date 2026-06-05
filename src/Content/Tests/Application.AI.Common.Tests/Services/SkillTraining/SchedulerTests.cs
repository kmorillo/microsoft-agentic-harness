using Application.AI.Common.Interfaces.SkillTraining;
using Application.AI.Common.Services.SkillTraining.Schedulers;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class SchedulerTests
{
    // ── Constant ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constant_ReturnsLrStartAtEveryStep()
    {
        ILrScheduler sut = new ConstantScheduler();
        for (int step = 0; step < 10; step++)
        {
            sut.GetLearningRate(step, totalSteps: 10, lrStart: 8, lrMin: 2).Should().Be(8);
        }
    }

    // ── Linear ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Linear_StartsAtLrStart_EndsAtLrMin()
    {
        ILrScheduler sut = new LinearScheduler();
        sut.GetLearningRate(0, totalSteps: 5, lrStart: 8, lrMin: 2).Should().Be(8);
        sut.GetLearningRate(4, totalSteps: 5, lrStart: 8, lrMin: 2).Should().Be(2);
    }

    [Fact]
    public void Linear_IsMonotonicallyNonIncreasing()
    {
        ILrScheduler sut = new LinearScheduler();
        var prev = int.MaxValue;
        for (int step = 0; step < 10; step++)
        {
            var lr = sut.GetLearningRate(step, totalSteps: 10, lrStart: 16, lrMin: 1);
            lr.Should().BeLessThanOrEqualTo(prev);
            prev = lr;
        }
    }

    [Fact]
    public void Linear_TotalStepsOne_ReturnsLrStart()
    {
        new LinearScheduler().GetLearningRate(0, totalSteps: 1, lrStart: 5, lrMin: 1).Should().Be(5);
    }

    // ── Cosine ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Cosine_StartsAtLrStart_EndsAtLrMin()
    {
        ILrScheduler sut = new CosineScheduler();
        sut.GetLearningRate(0, totalSteps: 9, lrStart: 16, lrMin: 2).Should().Be(16);
        sut.GetLearningRate(8, totalSteps: 9, lrStart: 16, lrMin: 2).Should().Be(2);
    }

    [Fact]
    public void Cosine_IsMonotonicallyNonIncreasing_AcrossWideRange()
    {
        ILrScheduler sut = new CosineScheduler();
        var prev = int.MaxValue;
        for (int step = 0; step < 20; step++)
        {
            var lr = sut.GetLearningRate(step, totalSteps: 20, lrStart: 16, lrMin: 1);
            lr.Should().BeLessThanOrEqualTo(prev);
            prev = lr;
        }
    }

    [Fact]
    public void Cosine_AtMidpoint_IsAroundHalfRange()
    {
        // For totalSteps=11 (10 intervals), step=5 → cos(pi/2)=0 → lr = lrMin + (lrStart-lrMin)*0.5
        // = 2 + 14*0.5 = 9
        new CosineScheduler().GetLearningRate(5, totalSteps: 11, lrStart: 16, lrMin: 2).Should().Be(9);
    }

    // ── Validation ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1, 5, 8, 2)]   // negative step
    [InlineData(0, 0, 8, 2)]    // zero totalSteps
    [InlineData(0, 5, 0, 1)]    // zero lrStart
    [InlineData(0, 5, 8, 0)]    // lrMin < 1
    [InlineData(0, 5, 4, 5)]    // lrMin > lrStart
    public void AllSchedulers_RejectInvalidArgs(int step, int totalSteps, int lrStart, int lrMin)
    {
        foreach (ILrScheduler sut in new ILrScheduler[]
                 { new ConstantScheduler(), new LinearScheduler(), new CosineScheduler() })
        {
            var act = () => sut.GetLearningRate(step, totalSteps, lrStart, lrMin);
            act.Should().Throw<ArgumentOutOfRangeException>(
                because: $"{sut.GetType().Name} must reject invalid args");
        }
    }
}
