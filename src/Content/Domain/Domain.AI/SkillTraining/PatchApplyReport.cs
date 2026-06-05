namespace Domain.AI.SkillTraining;

/// <summary>
/// The result of applying a <see cref="Patch"/> to a skill document.
/// </summary>
/// <remarks>
/// Carries both the new document content and a per-edit success/failure record so the
/// gate, audit trail, and meta-skill memory all see exactly which edits landed.
/// Edits that target text not present in the document are recorded as
/// <see cref="FailedEdits"/> rather than throwing.
/// </remarks>
public sealed record PatchApplyReport
{
    /// <summary>The skill document content after all successful edits have been applied.</summary>
    public required string NewSkillContent { get; init; }

    /// <summary>The edits that were applied successfully, in the order they were applied.</summary>
    public IReadOnlyList<Edit> AppliedEdits { get; init; } = [];

    /// <summary>The edits that could not be applied, paired with a reason string.</summary>
    public IReadOnlyList<FailedEdit> FailedEdits { get; init; } = [];

    /// <summary>
    /// True iff <see cref="NewSkillContent"/> differs from the original input the patch was
    /// applied to. Note that <see cref="AppliedEdits"/> being non-empty does <b>not</b> imply
    /// this — a Replace whose Target equals its Content is an applied no-op.
    /// </summary>
    public required bool HasChanges { get; init; }
}

/// <summary>
/// An edit that could not be applied, with the reason it was rejected.
/// </summary>
public sealed record FailedEdit
{
    /// <summary>The edit that failed.</summary>
    public required Edit Edit { get; init; }

    /// <summary>Human-readable reason the edit could not be applied (e.g. "target not found").</summary>
    public required string Reason { get; init; }
}
