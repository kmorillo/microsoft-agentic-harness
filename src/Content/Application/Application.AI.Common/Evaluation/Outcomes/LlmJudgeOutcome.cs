namespace Application.AI.Common.Evaluation.Outcomes;

/// <summary>
/// Why a call to <see cref="Interfaces.ILlmJudge.JudgeAsync"/> terminated.
/// </summary>
public enum LlmJudgeOutcome
{
    /// <summary>The judge returned valid JSON that deserialized into a score and reasoning.</summary>
    Parsed = 0,

    /// <summary>Both attempts (first + stricter retry) returned malformed JSON. Soft-fail to Warn.</summary>
    Malformed = 1,

    /// <summary>An infrastructure exception escaped the call (network, provider, etc.). Soft-fail to Warn.</summary>
    InvocationFailed = 2,
}
