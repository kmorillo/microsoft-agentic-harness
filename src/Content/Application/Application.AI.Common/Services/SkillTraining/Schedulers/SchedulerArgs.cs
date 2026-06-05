namespace Application.AI.Common.Services.SkillTraining.Schedulers;

/// <summary>
/// Shared argument validation for LR schedulers. Each scheduler calls
/// <see cref="Validate"/> at entry so the same misuses fail identically across implementations.
/// </summary>
internal static class SchedulerArgs
{
    /// <summary>
    /// Validates the four invariant arguments shared by every scheduler.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for negative <paramref name="step"/>, non-positive <paramref name="totalSteps"/>,
    /// non-positive <paramref name="lrStart"/>, or <paramref name="lrMin"/> outside
    /// [1, <paramref name="lrStart"/>].
    /// </exception>
    public static void Validate(int step, int totalSteps, int lrStart, int lrMin)
    {
        if (step < 0)
            throw new ArgumentOutOfRangeException(nameof(step), step, "step must be ≥ 0");
        if (totalSteps < 1)
            throw new ArgumentOutOfRangeException(nameof(totalSteps), totalSteps, "totalSteps must be ≥ 1");
        if (lrStart < 1)
            throw new ArgumentOutOfRangeException(nameof(lrStart), lrStart, "lrStart must be ≥ 1");
        if (lrMin < 1 || lrMin > lrStart)
            throw new ArgumentOutOfRangeException(nameof(lrMin), lrMin,
                $"lrMin must be in [1, {lrStart}]");
    }
}
