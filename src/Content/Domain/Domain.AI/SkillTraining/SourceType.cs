namespace Domain.AI.SkillTraining;

/// <summary>
/// Whether a proposed edit was derived from a failed rollout or a successful one.
/// </summary>
/// <remarks>
/// Most edits originate from <see cref="Failure"/> rollouts (the optimizer reasons
/// over what went wrong). <see cref="Success"/>-sourced edits capture patterns
/// worth reinforcing — for example, when a winning trajectory used a strategy
/// not currently codified in the skill document.
/// </remarks>
public enum SourceType
{
    /// <summary>The edit was proposed from analyzing a failed rollout trajectory.</summary>
    Failure,

    /// <summary>The edit was proposed from analyzing a successful rollout trajectory.</summary>
    Success
}
