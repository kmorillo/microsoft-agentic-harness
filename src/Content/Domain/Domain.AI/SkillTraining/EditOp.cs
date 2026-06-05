namespace Domain.AI.SkillTraining;

/// <summary>
/// The four bounded operations a skill-training edit can perform on a skill document.
/// </summary>
/// <remarks>
/// <para>
/// Bounded by design — the optimizer cannot rewrite the document arbitrarily, only
/// produce a sequence of these four operations. This mirrors the SkillOpt
/// methodology and keeps every change auditable, reversible, and aggregable.
/// </para>
/// <para>
/// All ops except <see cref="Append"/> require a non-empty <c>Target</c> string
/// that identifies the location in the document. If the target is not found the
/// edit fails (recorded in <c>PatchApplyReport.FailedEdits</c>) rather than
/// silently no-oping.
/// </para>
/// </remarks>
public enum EditOp
{
    /// <summary>Append <c>Content</c> to the end of the skill document. <c>Target</c> ignored.</summary>
    Append,

    /// <summary>Locate <c>Target</c> in the document and insert <c>Content</c> immediately after it.</summary>
    InsertAfter,

    /// <summary>Locate <c>Target</c> in the document and replace it with <c>Content</c>.</summary>
    Replace,

    /// <summary>Locate <c>Target</c> in the document and remove it. <c>Content</c> ignored.</summary>
    Delete
}
