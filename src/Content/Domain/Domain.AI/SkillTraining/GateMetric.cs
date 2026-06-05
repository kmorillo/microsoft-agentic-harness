namespace Domain.AI.SkillTraining;

/// <summary>
/// Which score the validation gate compares the candidate skill against the current/best on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Hard"/> uses exact-match (binary) accuracy and is the default — backward
/// compatible and the right choice when the selection set is large enough for
/// binary outcomes to be sensitive to incremental improvement.
/// </para>
/// <para>
/// <see cref="Soft"/> uses partial-credit / F1 / graded scores. Use when the selection
/// set is small and binary signal is too noisy.
/// </para>
/// <para>
/// <see cref="Mixed"/> takes a weighted average: <c>(1 - w) * hard + w * soft</c> where
/// <c>w</c> is configurable. Useful when neither pure metric is decisive on its own.
/// </para>
/// </remarks>
public enum GateMetric
{
    /// <summary>Exact-match (binary) score in [0, 1].</summary>
    Hard,

    /// <summary>Partial-credit / graded score in [0, 1].</summary>
    Soft,

    /// <summary>Weighted blend of hard and soft scores.</summary>
    Mixed
}
