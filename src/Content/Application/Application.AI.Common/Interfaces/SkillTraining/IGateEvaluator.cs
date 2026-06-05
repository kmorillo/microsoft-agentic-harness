using Domain.AI.SkillTraining;

namespace Application.AI.Common.Interfaces.SkillTraining;

/// <summary>
/// Pure validation gate for skill-training candidate skills.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors SkillOpt's <c>evaluation/gate.py</c>: a side-effect-free decision function
/// over (candidate score, current score, best score). The orchestrator (training loop)
/// owns all I/O — rolling out the candidate, computing scores, persisting checkpoints,
/// emitting telemetry. This interface stays a pure comparator so it can be unit-tested
/// exhaustively without engaging the agent stack.
/// </para>
/// <para>
/// Strictly greater-than semantics: ties with the current skill are <see cref="GateAction.Reject"/>
/// to avoid thrashing across equivalent skills that produce identical val-set scores by chance.
/// </para>
/// </remarks>
public interface IGateEvaluator
{
    /// <summary>
    /// Projects a candidate's (hard, soft) score pair onto a single comparable value
    /// using the configured <see cref="GateMetric"/>.
    /// </summary>
    /// <param name="hard">Exact-match (binary) score in [0, 1].</param>
    /// <param name="soft">Graded score in [0, 1].</param>
    /// <param name="metric">Which metric to project onto.</param>
    /// <param name="mixedWeight">
    /// Weight on <paramref name="soft"/> when <paramref name="metric"/> is <see cref="GateMetric.Mixed"/>.
    /// Must be in [0, 1] and finite. Ignored for <see cref="GateMetric.Hard"/> / <see cref="GateMetric.Soft"/>.
    /// Throws <see cref="ArgumentOutOfRangeException"/> on out-of-range or NaN when consulted.
    /// </param>
    double SelectGateScore(double hard, double soft, GateMetric metric, double mixedWeight = 0.5);

    /// <summary>
    /// Evaluates whether the candidate skill should be accepted, promoted to new best, or rejected.
    /// </summary>
    /// <param name="candidateSkill">The candidate skill document.</param>
    /// <param name="candidateHard">Aggregate hard score of the candidate on the selection set.</param>
    /// <param name="candidateSoft">Aggregate soft score of the candidate (ignored when <paramref name="metric"/> is Hard).</param>
    /// <param name="currentSkill">The currently-active skill at the moment of the comparison.</param>
    /// <param name="currentScore">Score (in metric space) of <paramref name="currentSkill"/>.</param>
    /// <param name="bestSkill">Best-so-far skill across the run.</param>
    /// <param name="bestScore">Score (in metric space) of <paramref name="bestSkill"/>.</param>
    /// <param name="bestStep">Training step at which the best was last updated.</param>
    /// <param name="globalStep">Current training step — recorded if a new best is promoted.</param>
    /// <param name="metric">Which metric to compare on.</param>
    /// <param name="mixedWeight">Soft-weight when <paramref name="metric"/> is Mixed.</param>
    /// <returns>The gate decision plus the new (current, best) state.</returns>
    GateResult Evaluate(
        string candidateSkill,
        double candidateHard,
        double candidateSoft,
        string currentSkill,
        double currentScore,
        string bestSkill,
        double bestScore,
        int bestStep,
        int globalStep,
        GateMetric metric,
        double mixedWeight = 0.5);
}
