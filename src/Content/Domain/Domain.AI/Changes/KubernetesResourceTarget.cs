namespace Domain.AI.Changes;

/// <summary>
/// A <see cref="ChangeTarget"/> identifying a Kubernetes resource managed via GitOps.
/// </summary>
/// <remarks>
/// <para>
/// The <c>MergeGate</c> for this target type never runs <c>kubectl apply</c> directly —
/// it writes the changed manifest into the GitOps repo (Argo / Flux) and lets the
/// GitOps controller reconcile the cluster. This keeps the cluster state declarative
/// and auditable through the GitOps repo history rather than through ad-hoc agent
/// API calls.
/// </para>
/// <para>
/// The four addressing fields (<see cref="ApiVersion"/>, <see cref="ResourceKind"/>,
/// <see cref="Namespace"/>, <see cref="ResourceName"/>) together uniquely identify the
/// resource within a cluster context. <see cref="ClusterContext"/> identifies which
/// cluster (matching the GitOps repo's overlay).
/// </para>
/// </remarks>
public sealed class KubernetesResourceTarget : ChangeTarget
{
    /// <summary>
    /// Construct a <see cref="KubernetesResourceTarget"/>.
    /// </summary>
    /// <param name="clusterContext">The cluster name or GitOps overlay this resource lives in (e.g. <c>prod-eastus</c>).</param>
    /// <param name="apiVersion">The Kubernetes API version (e.g. <c>apps/v1</c>, <c>v1</c>).</param>
    /// <param name="resourceKind">The resource kind (e.g. <c>Deployment</c>, <c>Service</c>, <c>ConfigMap</c>).</param>
    /// <param name="namespace">The namespace the resource lives in. Empty for cluster-scoped resources.</param>
    /// <param name="resourceName">The resource name.</param>
    public KubernetesResourceTarget(
        string clusterContext,
        string apiVersion,
        string resourceKind,
        string @namespace,
        string resourceName)
        : base(ChangeTargetKind.KubernetesResource, BuildDisplayName(clusterContext, resourceKind, @namespace, resourceName))
    {
        ClusterContext = clusterContext ?? string.Empty;
        ApiVersion = apiVersion ?? string.Empty;
        ResourceKind = resourceKind ?? string.Empty;
        Namespace = @namespace ?? string.Empty;
        ResourceName = resourceName ?? string.Empty;
    }

    /// <summary>The cluster name or GitOps overlay the resource lives in.</summary>
    public string ClusterContext { get; }

    /// <summary>The Kubernetes API version, e.g. <c>apps/v1</c>.</summary>
    public string ApiVersion { get; }

    /// <summary>The resource kind, e.g. <c>Deployment</c>.</summary>
    public string ResourceKind { get; }

    /// <summary>The namespace the resource lives in. Empty for cluster-scoped resources.</summary>
    public string Namespace { get; }

    /// <summary>The resource name.</summary>
    public string ResourceName { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Canonical form: <c>k8s:{clusterContext}/{apiVersion}/{namespace|cluster}/{resourceKind}/{resourceName}</c>.
    /// </remarks>
    public override string CanonicalKey()
    {
        var scope = string.IsNullOrEmpty(Namespace) ? "cluster" : Namespace;
        return $"k8s:{ClusterContext}/{ApiVersion}/{scope}/{ResourceKind}/{ResourceName}";
    }

    private static string BuildDisplayName(string clusterContext, string resourceKind, string @namespace, string resourceName)
    {
        if (string.IsNullOrEmpty(resourceKind) || string.IsNullOrEmpty(resourceName))
        {
            return "(unspecified kubernetes target)";
        }

        var prefix = string.IsNullOrEmpty(clusterContext) ? "?" : clusterContext;
        var scope = string.IsNullOrEmpty(@namespace) ? "cluster" : @namespace;
        return $"{prefix}/{scope}/{resourceKind}/{resourceName}";
    }
}
