namespace Domain.AI.GitOps;

/// <summary>
/// Discriminator identifying which GitOps controller a configured
/// <c>IGitOpsController</c> implementation talks to. Surfaces in audit lines,
/// drift reports, and remediation proposals so downstream consumers can branch
/// on the source controller without re-deriving it from the resolved instance
/// type (which is intentionally hidden behind the controller-neutral interface).
/// </summary>
/// <remarks>
/// <para>
/// PR-9 ships the two production-grade GitOps controllers in widest enterprise
/// use today; future controllers are added as new enum members and matching
/// <c>IGitOpsController</c> implementations registered behind their string-key.
/// </para>
/// <para>
/// The string form (lowercase) of each member is the keyed-DI service key used
/// to resolve the active <c>IGitOpsController</c> by configuration. Member
/// names follow PascalCase here per the codebase convention; consumers must
/// lower-case before resolving (or use the well-known constants surfaced by
/// the Application layer).
/// </para>
/// </remarks>
public enum GitOpsControllerKind
{
    /// <summary>
    /// Flux v2 (toolkit). The controller talks to a cluster running the Flux
    /// source-controller, kustomize-controller, helm-controller, and
    /// notification-controller deployments. Drift is read from
    /// <c>Kustomization</c> and <c>HelmRelease</c> custom resources.
    /// </summary>
    Flux = 0,

    /// <summary>
    /// Argo CD. The controller talks to the Argo CD API server (HTTPS) and
    /// reads drift from <c>Application</c> custom resources via the documented
    /// REST surface. The Argo CD API is the source of truth — the implementation
    /// never bypasses it by reading raw CRDs directly.
    /// </summary>
    ArgoCd = 1
}
