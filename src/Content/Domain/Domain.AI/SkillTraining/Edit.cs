namespace Domain.AI.SkillTraining;

/// <summary>
/// A single bounded edit to a skill document — the smallest unit of skill-training change.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="Edit"/> is conceptually analogous to one component of a gradient: it
/// names a location (<see cref="Target"/>), an operation (<see cref="Op"/>), and the
/// content to insert/replace. The optimizer emits these per rollout; aggregation,
/// selection, and gating decide which ones actually update the document.
/// </para>
/// <para>
/// <see cref="SupportCount"/> records how many trajectories supported this edit
/// (used during aggregation/selection). <see cref="MergeLevel"/> records the
/// depth at which this edit was merged from per-trajectory patches into the
/// final batch patch (0 = unmerged).
/// </para>
/// </remarks>
public sealed record Edit
{
    /// <summary>The operation this edit performs.</summary>
    public required EditOp Op { get; init; }

    /// <summary>
    /// The content to insert or replace. Ignored for <see cref="EditOp.Delete"/>;
    /// appended/inserted/used-as-replacement for the other three ops.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The text in the existing document this edit targets. Ignored for
    /// <see cref="EditOp.Append"/>; required for the other three ops.
    /// When the target cannot be located the edit is rejected (recorded in
    /// <c>PatchApplyReport.FailedEdits</c>).
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Number of trajectories that produced or supported this edit. Used by
    /// aggregation/selection — higher support = stronger gradient signal.
    /// Null when not yet computed.
    /// </summary>
    public int? SupportCount { get; init; }

    /// <summary>
    /// Whether this edit originated from a failed or successful rollout.
    /// Null when the source has not been classified.
    /// </summary>
    public SourceType? SourceType { get; init; }

    /// <summary>
    /// Merge depth of this edit. 0 means it has not been merged. On each aggregation pass that
    /// absorbs another edit into this one, the level is computed as <c>max(existing, incoming) + 1</c>
    /// (matching SkillOpt parity) — so the value reflects the depth of the deepest merge subtree,
    /// not the count of absorbed siblings. Used as a tie-breaker during selection when SupportCount
    /// ties.
    /// </summary>
    public int? MergeLevel { get; init; }

    /// <summary>
    /// Identifier of the rollout/trajectory this edit was derived from. Empty
    /// when not tracked. Used for provenance and debugging.
    /// </summary>
    public string UpdateOrigin { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the skill version this edit was intended to be applied to.
    /// Empty when not tracked. Helps detect stale edits proposed against an
    /// older skill that has since changed.
    /// </summary>
    public string UpdateTarget { get; init; } = string.Empty;
}
