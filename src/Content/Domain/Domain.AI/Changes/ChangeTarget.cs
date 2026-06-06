namespace Domain.AI.Changes;

/// <summary>
/// The thing a <see cref="ChangeProposal"/> wants to modify — a git repo, a Kubernetes
/// resource, an IaC deployment, etc. Abstract: every concrete target carries the
/// minimum identifying information for the <c>IChangeApplier</c> that will eventually
/// apply the diff.
/// </summary>
/// <remarks>
/// <para>
/// Class-based polymorphic hierarchy (not records) per the codebase coding-style rule
/// — records are reserved for simple value types; polymorphic types use classes with
/// factory methods so identity, equality, and subtype dispatch remain explicit.
/// </para>
/// <para>
/// New target types are added by subclassing this type and registering a matching
/// <c>IChangeApplier</c> with a keyed-DI key matching the new target's
/// <see cref="Kind"/> string. The <c>IChangeProposalGateResolver</c> uses
/// <see cref="Kind"/> to look up the default required-gates list per target type.
/// </para>
/// </remarks>
public abstract class ChangeTarget
{
    /// <summary>
    /// Construct a target with its discriminator and a stable display name.
    /// </summary>
    /// <param name="kind">The <see cref="ChangeTargetKind"/> discriminator for this subtype.</param>
    /// <param name="displayName">A short human-readable identifier for this target — repo url, resource name, deployment name. Used in audit lines and approval notifications.</param>
    protected ChangeTarget(ChangeTargetKind kind, string displayName)
    {
        Kind = kind;
        DisplayName = displayName ?? string.Empty;
    }

    /// <summary>The discriminator identifying which concrete subtype this is.</summary>
    public ChangeTargetKind Kind { get; }

    /// <summary>A short human-readable identifier — surfaces in audit lines, approval prompts, and orchestrator logs.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// A stable, content-derived string that uniquely identifies the target for
    /// deterministic-id derivation (Step 2). Two targets that mean the same thing
    /// must produce the same canonical form; two targets that mean different things
    /// must not collide.
    /// </summary>
    /// <returns>A canonical string for use as input to the proposal id hash.</returns>
    public abstract string CanonicalKey();
}
