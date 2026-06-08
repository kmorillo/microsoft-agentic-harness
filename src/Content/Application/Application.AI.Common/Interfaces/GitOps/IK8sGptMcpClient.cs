using Domain.Common;

namespace Application.AI.Common.Interfaces.GitOps;

/// <summary>
/// Client-side abstraction over the K8sGPT MCP server. The
/// <c>gitops-cluster-debug</c> skill consumes this to obtain LLM-shaped
/// root-cause analysis of cluster-side problems WITHOUT mutating the cluster.
/// </summary>
/// <remarks>
/// <para>
/// K8sGPT is a required dependency of the GitOps skill pack per the PR-9 plan
/// — it is NOT gracefully degraded. A consumer that enables the GitOps skill
/// pack without configuring a K8sGPT MCP server in
/// <c>AppConfig.AI.McpServers</c> gets a fail-loud startup error from
/// <c>GitOpsStartupValidator</c>. Degraded mode here would ship a half-working
/// skill — the plan explicitly rejects that.
/// </para>
/// <para>
/// The implementation MUST refuse to invoke any tool the K8sGPT MCP server
/// might expose that mutates the cluster. The K8sGPT analysis surface is
/// read-only; this client narrows it to read-only by contract regardless of
/// what the server advertises.
/// </para>
/// </remarks>
public interface IK8sGptMcpClient
{
    /// <summary>
    /// Ask the K8sGPT backend to analyze the cluster and return root-cause
    /// analysis findings.
    /// </summary>
    /// <param name="request">The analysis request — scope (namespace), filter (resource kinds), and explain flag.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> wrapping the structured analysis result
    /// on success; <see cref="Result{T}.Fail(string[])"/> with stable
    /// <c>gitops.k8sgpt.*</c> codes on MCP-side failure (unreachable, auth,
    /// schema mismatch).
    /// </returns>
    Task<Result<K8sGptAnalysisResult>> AnalyzeAsync(K8sGptAnalysisRequest request, CancellationToken cancellationToken);
}

/// <summary>Inputs to a K8sGPT analysis call.</summary>
public sealed record K8sGptAnalysisRequest
{
    /// <summary>Kubernetes namespace to scope the analysis to. Null means all namespaces.</summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Resource-kind filters (e.g. <c>"Deployment"</c>, <c>"Pod"</c>). Empty
    /// means analyze every kind K8sGPT supports for this cluster.
    /// </summary>
    public IReadOnlyList<string> Filters { get; init; } = [];

    /// <summary>
    /// True to request a natural-language explanation alongside the structured
    /// findings. Default true — the <c>gitops-cluster-debug</c> skill surfaces
    /// the explanation in its RCA report.
    /// </summary>
    public bool Explain { get; init; } = true;
}

/// <summary>The structured root-cause analysis K8sGPT returns for a single call.</summary>
public sealed record K8sGptAnalysisResult
{
    /// <summary>Wall-clock instant the analysis was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>The findings K8sGPT identified. Empty when the cluster has nothing to report.</summary>
    public IReadOnlyList<K8sGptFinding> Findings { get; init; } = [];

    /// <summary>
    /// The free-text explanation K8sGPT returned, when requested. Null when
    /// <see cref="K8sGptAnalysisRequest.Explain"/> was false or the backend
    /// returned no explanation.
    /// </summary>
    public string? Explanation { get; init; }
}

/// <summary>A single K8sGPT analysis finding.</summary>
public sealed record K8sGptFinding
{
    /// <summary>The Kubernetes kind the finding relates to (e.g. <c>Deployment</c>).</summary>
    public required string Kind { get; init; }

    /// <summary>The resource name the finding relates to.</summary>
    public required string Name { get; init; }

    /// <summary>The namespace the resource lives in; null for cluster-scoped resources.</summary>
    public string? Namespace { get; init; }

    /// <summary>Short, human-readable description of the finding.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>K8sGPT's severity classification of the finding (Low / Medium / High).</summary>
    public K8sGptSeverity Severity { get; init; } = K8sGptSeverity.Low;
}

/// <summary>Severity classification of a K8sGPT finding.</summary>
public enum K8sGptSeverity
{
    /// <summary>Cosmetic / informational — no action required.</summary>
    Low = 0,

    /// <summary>Behavioural issue in a bounded module — single resource degraded.</summary>
    Medium = 1,

    /// <summary>Cross-cutting impact — multiple resources affected or critical resource down.</summary>
    High = 2
}
