namespace Domain.AI.SkillTraining;

/// <summary>
/// The result of checking a <see cref="Patch"/> against the editable-surface fence before it is
/// applied or gated.
/// </summary>
/// <remarks>
/// A patch is allowed only when every one of its edits targets a surface the
/// <c>EditableSurfaceRegistry</c> marks editable. A single edit targeting a frozen surface fails the
/// whole patch — the fence is all-or-nothing, mirroring the bypass-immune denied-tool rule it is
/// modeled on.
/// </remarks>
public sealed record HarnessPatchValidation
{
    /// <summary>True iff no edit in the patch targets a frozen surface.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// The edits that targeted a frozen surface; empty when <see cref="IsAllowed"/> is true.
    /// </summary>
    public IReadOnlyList<FrozenSurfaceViolation> Violations { get; init; } = [];

    /// <summary>A shared allowed result carrying no violations.</summary>
    public static HarnessPatchValidation Allowed { get; } = new() { IsAllowed = true };
}

/// <summary>
/// A single edit the fence rejected because it targeted a frozen <see cref="HarnessSurface"/>.
/// </summary>
public sealed record FrozenSurfaceViolation
{
    /// <summary>The zero-based index of the offending edit within the patch's edit list.</summary>
    public required int EditIndex { get; init; }

    /// <summary>The frozen surface the edit attempted to target.</summary>
    public required HarnessSurface Surface { get; init; }

    /// <summary>
    /// True when the surface is frozen <em>by construction</em> (denied tools, autonomy tier, content
    /// safety, the registry itself) — a freeze no configuration or future widening can lift — versus
    /// frozen merely by current policy. Recorded so the audit trail distinguishes a policy rejection
    /// from an attempt to breach a hard governance boundary.
    /// </summary>
    public required bool FrozenByConstruction { get; init; }
}
