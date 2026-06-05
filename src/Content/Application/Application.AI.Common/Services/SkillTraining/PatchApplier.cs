using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// Pure service that applies a <see cref="Patch"/> to a skill document and returns a
/// <see cref="PatchApplyReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stateless and deterministic — no I/O, no logging, no DI dependencies. Suitable for
/// hot-path use during a training loop and trivially unit-testable. Mirrors the
/// behavior of SkillOpt's <c>optimizer/skill.py apply_patch_with_report</c>.
/// </para>
/// <para>
/// Edits are applied in order; each edit operates on the document produced by the
/// previous successful edit. An edit that targets text not present in the document
/// is rejected and recorded in <see cref="PatchApplyReport.FailedEdits"/> rather
/// than throwing, so the caller can decide whether the residual partial patch is
/// still worth gating.
/// </para>
/// <para>
/// <b>Separator policy.</b> The applier never injects whitespace beyond the single
/// blank line between an existing non-empty document and an <see cref="EditOp.Append"/>'s
/// content. <see cref="EditOp.InsertAfter"/> inserts <see cref="Edit.Content"/> verbatim
/// immediately after the located target — the optimizer is responsible for embedding
/// any required leading newline in <see cref="Edit.Content"/>. This matches the SkillOpt
/// convention where <see cref="Edit.Target"/> already encodes its own terminator
/// (e.g. <c>"- removed rule\n"</c>).
/// </para>
/// <para>
/// <b>Empty-content policy.</b> <see cref="EditOp.Append"/>, <see cref="EditOp.InsertAfter"/>,
/// and <see cref="EditOp.Replace"/> all reject empty <see cref="Edit.Content"/> as a failed
/// edit. An LLM that intends to remove text must emit <see cref="EditOp.Delete"/> explicitly —
/// silent "Replace with empty" would launder a removal as a content change and corrupt the
/// audit trail.
/// </para>
/// <para>
/// <see cref="EditOp.InsertAfter"/>, <see cref="EditOp.Replace"/>, and
/// <see cref="EditOp.Delete"/> match the <i>first</i> occurrence of the target.
/// Optimizers that need to target a specific occurrence should embed surrounding
/// context in <see cref="Edit.Target"/>.
/// </para>
/// </remarks>
public sealed class PatchApplier
{
    private const string TargetNotFoundReason = "target not found in current skill document";
    private const string TargetRequiredReason = "target required for this edit op";
    private const string ContentRequiredReason = "empty content; use Delete to remove text";

    /// <summary>
    /// Applies the patch to the document and returns a per-edit report.
    /// </summary>
    /// <param name="currentSkillContent">The skill document the patch is being applied to.</param>
    /// <param name="patch">The patch whose edits will be applied in order.</param>
    /// <returns>A report describing which edits applied and the resulting document.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="currentSkillContent"/> or <paramref name="patch"/> is null.
    /// </exception>
    public PatchApplyReport Apply(string currentSkillContent, Patch patch)
    {
        ArgumentNullException.ThrowIfNull(currentSkillContent);
        ArgumentNullException.ThrowIfNull(patch);

        var working = currentSkillContent;
        var applied = new List<Edit>(patch.Edits.Count);
        var failed = new List<FailedEdit>();

        foreach (var edit in patch.Edits)
        {
            var (next, ok, reason) = TryApply(working, edit);
            if (ok)
            {
                working = next;
                applied.Add(edit);
            }
            else
            {
                failed.Add(new FailedEdit { Edit = edit, Reason = reason! });
            }
        }

        return new PatchApplyReport
        {
            NewSkillContent = working,
            AppliedEdits = applied,
            FailedEdits = failed,
            HasChanges = !string.Equals(working, currentSkillContent, StringComparison.Ordinal)
        };
    }

    private static (string Next, bool Ok, string? Reason) TryApply(string content, Edit edit)
    {
        // Content-required check covers Append/InsertAfter/Replace; Delete ignores Content.
        if (edit.Op is EditOp.Append or EditOp.InsertAfter or EditOp.Replace
            && string.IsNullOrEmpty(edit.Content))
        {
            return (content, false, ContentRequiredReason);
        }

        // Target-required + locate covers InsertAfter/Replace/Delete; Append ignores Target.
        int targetIdx = -1;
        if (edit.Op is EditOp.InsertAfter or EditOp.Replace or EditOp.Delete)
        {
            if (string.IsNullOrEmpty(edit.Target))
            {
                return (content, false, TargetRequiredReason);
            }
            targetIdx = content.IndexOf(edit.Target, StringComparison.Ordinal);
            if (targetIdx < 0)
            {
                return (content, false, TargetNotFoundReason);
            }
        }

        return edit.Op switch
        {
            EditOp.Append => (
                content.Length == 0 ? edit.Content : content + "\n\n" + edit.Content,
                true, null),

            EditOp.InsertAfter => (
                content[..(targetIdx + edit.Target.Length)] + edit.Content
                    + content[(targetIdx + edit.Target.Length)..],
                true, null),

            EditOp.Replace => (
                content[..targetIdx] + edit.Content
                    + content[(targetIdx + edit.Target.Length)..],
                true, null),

            EditOp.Delete => (
                content[..targetIdx] + content[(targetIdx + edit.Target.Length)..],
                true, null),

            _ => (content, false, $"unknown edit op: {edit.Op}")
        };
    }
}
