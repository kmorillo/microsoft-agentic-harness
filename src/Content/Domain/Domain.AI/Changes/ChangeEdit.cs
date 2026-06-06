using Domain.AI.SkillTraining;

namespace Domain.AI.Changes;

/// <summary>
/// A single bounded edit inside a <see cref="ChangeProposal"/>'s diff. Names a
/// location (<see cref="Target"/>), an operation (<see cref="Op"/>), and the content
/// to insert or replace.
/// </summary>
/// <remarks>
/// <para>
/// Shares the four <see cref="EditOp"/> values with <c>Domain.AI.SkillTraining</c> so
/// the harness has one bounded-edit vocabulary. Distinct from <see cref="Edit"/>
/// because that record carries SkillOpt-specific provenance fields (support count,
/// merge level, update origin) that are irrelevant outside the optimizer.
/// </para>
/// <para>
/// Order matters: a <see cref="ChangeProposal"/>'s diff is an ordered list, applied
/// in sequence. Two diffs with identical edits in a different order are not equal
/// and produce different deterministic proposal ids.
/// </para>
/// <para>
/// <see cref="EditOp.Append"/> ignores <see cref="Target"/>; the other three ops
/// require a non-empty <see cref="Target"/> to locate the change site.
/// <see cref="EditOp.Delete"/> ignores <see cref="Content"/>; the other three use it.
/// Validators enforce these invariants at the gate layer, not at the record itself.
/// </para>
/// </remarks>
public sealed record ChangeEdit
{
    /// <summary>The operation this edit performs (Append / InsertAfter / Replace / Delete).</summary>
    public required EditOp Op { get; init; }

    /// <summary>
    /// The content to insert or use as the replacement. Ignored for
    /// <see cref="EditOp.Delete"/>; required for the other three.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The location in the target the edit applies at. Ignored for
    /// <see cref="EditOp.Append"/>; required for the other three.
    /// </summary>
    public string Target { get; init; } = string.Empty;
}
