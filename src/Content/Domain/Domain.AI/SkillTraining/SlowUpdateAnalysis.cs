namespace Domain.AI.SkillTraining;

/// <summary>
/// Longitudinal comparison of two skill versions over the same item ids — port of SkillOpt's
/// slow-update mechanism.
/// </summary>
/// <remarks>
/// Records how many items the new skill improved, regressed, or left unchanged versus the
/// prior skill. Used by the optimizer at end-of-epoch to generate cross-epoch guidance and
/// to detect catastrophic forgetting.
/// </remarks>
public sealed record SlowUpdateAnalysis
{
    /// <summary>Items that failed under prior skill (Hard&lt;1) and pass under new skill (Hard==1).</summary>
    public required int Improved { get; init; }

    /// <summary>Items that passed under prior skill and fail under new skill — forgetting signal.</summary>
    public required int Regressed { get; init; }

    /// <summary>Items that fail under both skill versions.</summary>
    public required int PersistentFail { get; init; }

    /// <summary>Items that pass under both skill versions.</summary>
    public required int StableSuccess { get; init; }

    /// <summary>Total paired items the analysis covered (sum of the four categories).</summary>
    public int Total => Improved + Regressed + PersistentFail + StableSuccess;

    /// <summary>Natural-language guidance synthesized from the counts — suitable to inject into the optimizer's next-epoch context.</summary>
    public required string Guidance { get; init; }
}
