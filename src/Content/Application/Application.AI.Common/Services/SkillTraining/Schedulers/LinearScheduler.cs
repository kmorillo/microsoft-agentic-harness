using Application.AI.Common.Interfaces.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining.Schedulers;

/// <summary>
/// Linear-decay learning-rate scheduler — interpolates from <c>lrStart</c> at step 0 down
/// to <c>lrMin</c> at the final step.
/// </summary>
public sealed class LinearScheduler : ILrScheduler
{
    /// <inheritdoc />
    public int GetLearningRate(int step, int totalSteps, int lrStart, int lrMin)
    {
        SchedulerArgs.Validate(step, totalSteps, lrStart, lrMin);

        // At step=0 → lrStart; at step=totalSteps-1 → lrMin.
        // For totalSteps==1 the formula degenerates; return lrStart.
        if (totalSteps == 1) return lrStart;

        var t = Math.Min(step, totalSteps - 1) / (double)(totalSteps - 1);
        var value = lrStart - t * (lrStart - lrMin);
        return (int)Math.Round(Math.Clamp(value, lrMin, lrStart));
    }
}
