using Application.AI.Common.Interfaces.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining.Schedulers;

/// <summary>
/// Cosine-annealing learning-rate scheduler — preserves the high-LR early-exploration
/// followed by smooth taper that the SkillOpt paper recommends as the strongest default.
/// </summary>
/// <remarks>
/// Formula: <c>lr(step) = lrMin + (lrStart - lrMin) * 0.5 * (1 + cos(pi * step / (totalSteps - 1)))</c>.
/// At step 0 → <c>lrStart</c>; at step <c>totalSteps - 1</c> → <c>lrMin</c>.
/// </remarks>
public sealed class CosineScheduler : ILrScheduler
{
    /// <inheritdoc />
    public int GetLearningRate(int step, int totalSteps, int lrStart, int lrMin)
    {
        SchedulerArgs.Validate(step, totalSteps, lrStart, lrMin);

        if (totalSteps == 1) return lrStart;

        var clamped = Math.Min(step, totalSteps - 1);
        var t = clamped / (double)(totalSteps - 1);
        var coeff = 0.5 * (1.0 + Math.Cos(Math.PI * t));
        var value = lrMin + (lrStart - lrMin) * coeff;
        return (int)Math.Round(Math.Clamp(value, lrMin, lrStart));
    }
}
