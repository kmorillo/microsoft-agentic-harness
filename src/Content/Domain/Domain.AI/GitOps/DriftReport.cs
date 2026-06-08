namespace Domain.AI.GitOps;

/// <summary>
/// A controller-neutral snapshot of drift between desired state (declared in Git)
/// and actual state (running in the cluster). Produced by
/// <c>IGitOpsController.DetectDriftAsync</c>. The same shape is returned by both
/// the Flux and ArgoCD implementations so consumers can author drift-handling
/// logic once.
/// </summary>
/// <remarks>
/// <para>
/// Records the controller-of-origin via <see cref="ControllerKind"/> so audit
/// lines and remediation proposals carry that breadcrumb forward — the
/// controller-neutral abstraction is for callers, not for the audit trail.
/// </para>
/// <para>
/// <see cref="EvidenceHash"/> is a content-addressed digest of the raw controller
/// response that produced this report. The harness's evidence store keys gate
/// findings on this hash, so a drift report can be referenced from a
/// <c>ChangeProposal</c>'s audit chain without re-fetching the controller's
/// response.
/// </para>
/// </remarks>
public sealed record DriftReport
{
    /// <summary>The controller that produced this report.</summary>
    public required GitOpsControllerKind ControllerKind { get; init; }

    /// <summary>
    /// Wall-clock instant the report was captured. Used in audit lines and to
    /// detect stale reports the remediation pipeline should refuse to act on.
    /// </summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    /// The set of resources whose live state diverges from the Git-declared
    /// desired state. Empty when the cluster is fully reconciled.
    /// </summary>
    public IReadOnlyList<DriftedResource> DriftedResources { get; init; } = [];

    /// <summary>
    /// Content-addressed digest of the raw controller response that produced this
    /// report. Stable across captures of identical responses; used as the
    /// evidence key when this report surfaces in a <c>ChangeProposal</c>.
    /// </summary>
    public string EvidenceHash { get; init; } = string.Empty;

    /// <summary>True when at least one resource is drifted.</summary>
    public bool HasDrift => DriftedResources.Count > 0;
}

/// <summary>
/// A single resource whose actual state differs from its Git-declared desired
/// state. Controller-neutral — the implementation maps Flux <c>Kustomization</c>
/// status fields or Argo CD <c>Application</c> sync status fields onto this
/// shared shape.
/// </summary>
public sealed record DriftedResource
{
    /// <summary>The resource's Kubernetes API group/version (e.g. <c>apps/v1</c>).</summary>
    public required string ApiVersion { get; init; }

    /// <summary>The resource's Kubernetes kind (e.g. <c>Deployment</c>, <c>HelmRelease</c>).</summary>
    public required string Kind { get; init; }

    /// <summary>The resource's namespace; null for cluster-scoped resources.</summary>
    public string? Namespace { get; init; }

    /// <summary>The resource name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The Git path the desired state was declared at, when the controller
    /// reports it. Used by remediation to identify which file in the Git repo
    /// the <c>ChangeProposal</c> should target.
    /// </summary>
    public string? DesiredPath { get; init; }

    /// <summary>
    /// Short, human-readable description of how this resource diverges. Surfaces
    /// in drift reports and remediation summaries.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Severity band for prioritization: Low / Medium / High. The drift detector
    /// maps controller-specific severity signals (e.g. Argo CD <c>HealthStatus</c>
    /// or Flux <c>Suspended</c>) onto this neutral scale.
    /// </summary>
    public DriftSeverity Severity { get; init; } = DriftSeverity.Low;
}

/// <summary>
/// Coarse prioritization band for a <see cref="DriftedResource"/>. Drives
/// remediation default <c>BlastRadius</c> and the order in which the remediation
/// pipeline addresses concurrent drift.
/// </summary>
public enum DriftSeverity
{
    /// <summary>Cosmetic divergence — annotations, labels, replicas within HPA range.</summary>
    Low = 0,

    /// <summary>Behavioural divergence in a bounded module — single Deployment image mismatch, ConfigMap key changed.</summary>
    Medium = 1,

    /// <summary>Cross-cutting or public-contract divergence — CRD spec mismatch, Ingress host change, Secret type drift.</summary>
    High = 2
}
