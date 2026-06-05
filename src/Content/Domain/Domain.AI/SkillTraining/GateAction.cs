namespace Domain.AI.SkillTraining;

/// <summary>
/// The decision the validation gate makes for a candidate skill.
/// </summary>
/// <remarks>
/// Modeled on the <c>action</c> field of SkillOpt's <c>GateResult</c>:
/// strictly better than the running best → <see cref="AcceptNewBest"/>;
/// equal-or-better than current but not the all-time best → <see cref="Accept"/>;
/// worse than current → <see cref="Reject"/>. Patience-based early stopping
/// uses the count of consecutive <see cref="Reject"/>s.
/// </remarks>
public enum GateAction
{
    /// <summary>Candidate strictly beats the running best — promote it.</summary>
    AcceptNewBest,

    /// <summary>Candidate matches or beats current but does not exceed running best.</summary>
    Accept,

    /// <summary>Candidate is worse than current — discard.</summary>
    Reject
}
