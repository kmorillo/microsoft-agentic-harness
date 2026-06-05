using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// Pure aggregator: collapses a list of per-item <see cref="RolloutResult"/>s into a single
/// (hard, soft) batch score by mean.
/// </summary>
/// <remarks>
/// Used wherever the orchestrator needs to project a rollout batch onto the gate's
/// (hard, soft) input space. Empty batches produce (0, 0) — callers should reject upstream.
/// </remarks>
public static class RolloutBatchScorer
{
    /// <summary>
    /// Returns the mean hard and soft scores across the rollouts.
    /// </summary>
    /// <param name="rollouts">The per-item rollout results.</param>
    /// <returns>Tuple of (hardMean, softMean), both in [0, 1]. <c>(0.0, 0.0)</c> for empty input.</returns>
    public static (double Hard, double Soft) Score(IReadOnlyList<RolloutResult> rollouts)
    {
        ArgumentNullException.ThrowIfNull(rollouts);
        if (rollouts.Count == 0) return (0.0, 0.0);

        double hardSum = 0;
        double softSum = 0;
        foreach (var r in rollouts)
        {
            hardSum += r.Hard;
            softSum += r.Soft;
        }
        var n = rollouts.Count;
        return (hardSum / n, softSum / n);
    }
}
