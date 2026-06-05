using Application.AI.Common.Interfaces.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining.Schedulers;

/// <summary>
/// Constant learning-rate scheduler — returns <c>lrStart</c> regardless of step.
/// </summary>
/// <remarks>
/// Useful as a baseline and for sanity-checking the rest of the training loop. Per the
/// SkillOpt paper this generally underperforms cosine/linear but is the right choice when
/// total steps is very small and decay would prevent the loop from making progress.
/// </remarks>
public sealed class ConstantScheduler : ILrScheduler
{
    /// <inheritdoc />
    public int GetLearningRate(int step, int totalSteps, int lrStart, int lrMin)
    {
        SchedulerArgs.Validate(step, totalSteps, lrStart, lrMin);
        return lrStart;
    }
}
