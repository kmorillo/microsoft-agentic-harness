namespace Application.AI.Common.Interfaces.SkillTraining;

/// <summary>
/// Computes the number of edits to apply at a given training step — analogous to a
/// deep-learning learning-rate scheduler over (step, total_steps).
/// </summary>
/// <remarks>
/// <para>
/// Port of SkillOpt's <c>optimizer/scheduler.py</c>: the "learning rate" controls how many
/// edits are applied per step. Higher early, smaller late (cosine / linear decay) tends to
/// outperform constant in SkillOpt's experiments. Output is integer because edits are
/// discrete — a fractional rate would not be meaningful.
/// </para>
/// <para>
/// Implementations are pure; no state, no I/O. The scheduler is interrogated at each step
/// by the training orchestrator.
/// </para>
/// </remarks>
public interface ILrScheduler
{
    /// <summary>
    /// Returns the maximum number of edits to apply at <paramref name="step"/>.
    /// </summary>
    /// <param name="step">Current step (0-based).</param>
    /// <param name="totalSteps">Total steps in the training run (must be ≥ 1).</param>
    /// <param name="lrStart">LR at step 0 (must be ≥ 1).</param>
    /// <param name="lrMin">Floor LR (must be ≥ 1 and ≤ <paramref name="lrStart"/>).</param>
    /// <returns>Clipped edit budget in [<paramref name="lrMin"/>, <paramref name="lrStart"/>].</returns>
    int GetLearningRate(int step, int totalSteps, int lrStart, int lrMin);
}
