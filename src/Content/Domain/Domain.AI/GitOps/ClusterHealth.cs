namespace Domain.AI.GitOps;

/// <summary>
/// A controller-neutral snapshot of cluster-level health as seen by the active
/// GitOps controller. Produced by <c>IGitOpsController.GetClusterHealthAsync</c>
/// and consumed by the <c>gitops-cluster-debug</c> skill as one of the two
/// signal sources for RCA (the other being K8sGPT).
/// </summary>
/// <remarks>
/// <para>
/// This type is deliberately read-only and observational — neither Flux nor
/// Argo CD-specific health primitives leak through. The implementation maps
/// controller-native concepts (Flux <c>Suspended</c> + <c>Stalled</c> +
/// <c>Ready</c> conditions, Argo CD <c>HealthStatus</c> + <c>SyncStatus</c>)
/// onto this neutral surface.
/// </para>
/// </remarks>
public sealed record ClusterHealth
{
    /// <summary>The controller that produced this snapshot.</summary>
    public required GitOpsControllerKind ControllerKind { get; init; }

    /// <summary>Wall-clock instant the snapshot was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Overall cluster status — neutral roll-up across all watched resources.</summary>
    public ClusterHealthStatus OverallStatus { get; init; } = ClusterHealthStatus.Unknown;

    /// <summary>
    /// Per-resource health entries. Empty when the controller reports the cluster
    /// is fully reconciled and healthy.
    /// </summary>
    public IReadOnlyList<ResourceHealth> ResourceStates { get; init; } = [];

    /// <summary>
    /// Free-form notes the controller surfaced at the cluster level (e.g. Flux
    /// notification-controller backlog warnings, Argo CD ApplicationSet errors).
    /// Surfaces verbatim in <c>gitops-cluster-debug</c> reports.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>The neutral roll-up status of a <see cref="ClusterHealth"/> snapshot.</summary>
public enum ClusterHealthStatus
{
    /// <summary>The controller's status is not yet known — initial call hasn't returned.</summary>
    Unknown = 0,

    /// <summary>Every watched resource is Ready and Sync'd.</summary>
    Healthy = 1,

    /// <summary>At least one resource is Progressing — converging toward desired state, no failures.</summary>
    Progressing = 2,

    /// <summary>At least one resource is Degraded — reachable but unhealthy.</summary>
    Degraded = 3,

    /// <summary>At least one resource is in a hard-failure state — Stalled (Flux) or Failed (Argo CD).</summary>
    Failed = 4
}

/// <summary>A single resource's health entry inside a <see cref="ClusterHealth"/> snapshot.</summary>
public sealed record ResourceHealth
{
    /// <summary>The resource's Kubernetes API group/version.</summary>
    public required string ApiVersion { get; init; }

    /// <summary>The resource's Kubernetes kind.</summary>
    public required string Kind { get; init; }

    /// <summary>The resource's namespace; null for cluster-scoped resources.</summary>
    public string? Namespace { get; init; }

    /// <summary>The resource name.</summary>
    public required string Name { get; init; }

    /// <summary>The resource's roll-up status.</summary>
    public ClusterHealthStatus Status { get; init; } = ClusterHealthStatus.Unknown;

    /// <summary>Short human-readable message explaining the status.</summary>
    public string Message { get; init; } = string.Empty;
}
