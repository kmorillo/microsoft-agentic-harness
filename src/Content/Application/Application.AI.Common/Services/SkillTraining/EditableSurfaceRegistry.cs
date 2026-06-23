using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// The code-owned allowlist of which <see cref="HarnessSurface"/>s the skill-training loop may edit —
/// the fence at the heart of Self-Harness Phase 1.
/// </summary>
/// <remarks>
/// <para>
/// The registry is <em>owned by code, not by the loop</em>: the optimizer can propose edits, but it
/// can never change what this registry permits. Today only <see cref="HarnessSurface.SkillDocument"/>
/// is editable; every other surface is frozen.
/// </para>
/// <para>
/// A subset of surfaces is frozen <em>by construction</em> — <see cref="HarnessSurface.DeniedTools"/>,
/// <see cref="HarnessSurface.AutonomyTier"/>, <see cref="HarnessSurface.ContentSafetyConfig"/>, and
/// <see cref="HarnessSurface.EditableSurfaceRegistry"/>. These can never be made editable: the
/// widening constructor throws if asked to include any of them. This is the analog, for
/// self-modification, of the bypass-immune denied-tool rule — a guarantee enforced in the type system
/// rather than in configuration that could be edited or misconfigured.
/// </para>
/// </remarks>
public sealed class EditableSurfaceRegistry
{
    private static readonly IReadOnlySet<HarnessSurface> AlwaysFrozenSurfaces =
        new HashSet<HarnessSurface>
        {
            HarnessSurface.DeniedTools,
            HarnessSurface.AutonomyTier,
            HarnessSurface.ContentSafetyConfig,
            HarnessSurface.EditableSurfaceRegistry
        };

    private readonly IReadOnlySet<HarnessSurface> _editableSurfaces;

    /// <summary>
    /// Initializes the registry with the default policy: only
    /// <see cref="HarnessSurface.SkillDocument"/> is editable. This is the parameterless constructor
    /// dependency injection resolves.
    /// </summary>
    public EditableSurfaceRegistry()
        : this([HarnessSurface.SkillDocument])
    {
    }

    /// <summary>
    /// Initializes the registry with an explicit set of editable surfaces. Reserved for a future phase
    /// that widens the loop's edit target behind a product decision; today only the default set is
    /// wired.
    /// </summary>
    /// <param name="editableSurfaces">The surfaces the loop may edit.</param>
    /// <exception cref="ArgumentNullException"><paramref name="editableSurfaces"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Any requested surface is frozen by construction (<see cref="IsFrozenByConstruction"/>) and can
    /// never be made editable.
    /// </exception>
    public EditableSurfaceRegistry(IEnumerable<HarnessSurface> editableSurfaces)
    {
        ArgumentNullException.ThrowIfNull(editableSurfaces);

        var requested = new HashSet<HarnessSurface>(editableSurfaces);
        var illegal = requested.Where(AlwaysFrozenSurfaces.Contains).ToArray();
        if (illegal.Length > 0)
        {
            throw new ArgumentException(
                $"Surface(s) [{string.Join(", ", illegal)}] are frozen by construction and can never be marked editable.",
                nameof(editableSurfaces));
        }

        _editableSurfaces = requested;
    }

    /// <summary>Returns <see langword="true"/> iff the loop may edit <paramref name="surface"/>.</summary>
    /// <param name="surface">The surface to test.</param>
    public bool IsEditable(HarnessSurface surface) => _editableSurfaces.Contains(surface);

    /// <summary>
    /// Returns <see langword="true"/> iff <paramref name="surface"/> is frozen by construction — a hard
    /// governance boundary (denied tools, autonomy tier, content safety, the registry itself) that no
    /// configuration or widening can ever make editable.
    /// </summary>
    /// <param name="surface">The surface to test.</param>
    public bool IsFrozenByConstruction(HarnessSurface surface) => AlwaysFrozenSurfaces.Contains(surface);
}
