namespace Domain.AI.SkillTraining;

/// <summary>
/// An ordered batch of <see cref="Edit"/>s the optimizer proposes against a skill document,
/// together with the reasoning behind them.
/// </summary>
/// <remarks>
/// <para>
/// A patch is the unit produced by <c>ReflectOnFailuresCommand</c> and consumed by
/// the aggregate → select → apply pipeline. The <see cref="Edits"/> list is order-significant:
/// the applier walks it top to bottom, applying each edit to the document produced by the
/// previous one.
/// </para>
/// <para>
/// <see cref="Reasoning"/> captures the optimizer's narrative for why this batch was
/// proposed — preserved verbatim for audit trails and as input to meta-skill memory.
/// </para>
/// </remarks>
public sealed record Patch
{
    /// <summary>The ordered edits in this patch.</summary>
    public IReadOnlyList<Edit> Edits { get; init; } = [];

    /// <summary>Natural-language explanation of why these edits were proposed.</summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// Optional ranking metadata attached by selection (e.g. per-edit scores,
    /// tie-breakers, the LR snapshot at the moment of selection). Free-form bag.
    /// </summary>
    public IReadOnlyDictionary<string, string>? RankingDetails { get; init; }
}
