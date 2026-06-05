using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// Pure port of SkillOpt's <c>evaluation/gate.py</c>. Stateless, deterministic, no I/O.
/// </summary>
public sealed class GateEvaluator : IGateEvaluator
{
    /// <inheritdoc />
    public double SelectGateScore(double hard, double soft, GateMetric metric, double mixedWeight = 0.5)
    {
        EnsureFinite(hard, nameof(hard));
        EnsureFinite(soft, nameof(soft));

        return metric switch
        {
            GateMetric.Hard => hard,
            GateMetric.Soft => soft,
            GateMetric.Mixed => Project(hard, soft, mixedWeight),
            _ => throw new ArgumentOutOfRangeException(
                nameof(metric), metric, $"unknown gate metric: {metric}")
        };

        static double Project(double hard, double soft, double weight)
        {
            if (double.IsNaN(weight) || weight < 0.0 || weight > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(weight), weight, "mixedWeight must be in [0, 1] and not NaN.");
            }
            return (1.0 - weight) * hard + weight * soft;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Floating-point note: the orchestrator should re-project current/best scores via
    /// <see cref="SelectGateScore"/> on every call rather than persisting projected
    /// values, so a candidate that ties an earlier accepted skill compares bit-identically.
    /// Persisting projected scores across checkpoint round-trips (REAL → text → REAL)
    /// can flip Accept/Reject by a 1-ULP rounding difference.
    /// </para>
    /// </remarks>
    public GateResult Evaluate(
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
        double mixedWeight = 0.5)
    {
        ArgumentNullException.ThrowIfNull(candidateSkill);
        ArgumentNullException.ThrowIfNull(currentSkill);
        ArgumentNullException.ThrowIfNull(bestSkill);
        EnsureFinite(currentScore, nameof(currentScore));
        EnsureFinite(bestScore, nameof(bestScore));

        // candidateHard/Soft finite-check happens inside SelectGateScore.
        var candidateScore = SelectGateScore(candidateHard, candidateSoft, metric, mixedWeight);

        if (candidateScore > currentScore)
        {
            if (candidateScore > bestScore)
            {
                return new GateResult
                {
                    Action = GateAction.AcceptNewBest,
                    CurrentSkill = candidateSkill,
                    CurrentScore = candidateScore,
                    BestSkill = candidateSkill,
                    BestScore = candidateScore,
                    BestStep = globalStep,
                    CandidateSkill = candidateSkill,
                    CandidateScore = candidateScore
                };
            }

            return new GateResult
            {
                Action = GateAction.Accept,
                CurrentSkill = candidateSkill,
                CurrentScore = candidateScore,
                BestSkill = bestSkill,
                BestScore = bestScore,
                BestStep = bestStep,
                CandidateSkill = candidateSkill,
                CandidateScore = candidateScore
            };
        }

        return new GateResult
        {
            Action = GateAction.Reject,
            CurrentSkill = currentSkill,
            CurrentScore = currentScore,
            BestSkill = bestSkill,
            BestScore = bestScore,
            BestStep = bestStep,
            CandidateSkill = candidateSkill,
            CandidateScore = candidateScore
        };
    }

    private static void EnsureFinite(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException(
                $"score must be finite (got {value}); a NaN/Infinity here almost always indicates a corrupted upstream aggregation.",
                paramName);
        }
    }
}
