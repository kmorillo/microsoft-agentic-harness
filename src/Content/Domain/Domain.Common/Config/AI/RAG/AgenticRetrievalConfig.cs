namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for the optional Azure AI Search <em>agentic retrieval</em> backend, which
/// delegates the entire query → ranked-results step to an Azure AI Search knowledge base
/// (server-side query execution and semantic ranking) instead of the harness's local
/// dense + sparse + RRF hybrid pipeline. Bound from <c>AppConfig:AI:Rag:AgenticRetrieval</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Opt-in and off by default.</strong> When <see cref="Enabled"/> is <c>false</c> the
/// harness uses its portable local hybrid retriever. When <c>true</c>, the
/// <c>IHybridRetriever</c> resolves to the Azure knowledge-base implementation instead.
/// </para>
/// <para>
/// This targets the <strong>stable</strong> <c>Azure.Search.Documents</c> GA surface
/// (<c>KnowledgeBaseRetrievalClient</c>, API version 2026-04-01), which performs parallel
/// extractive retrieval with semantic ranking. The LLM query-planning and answer-synthesis
/// features of agentic retrieval are preview-only and intentionally NOT used here, so no
/// preview dependency is introduced. The referenced knowledge base must already exist on the
/// Azure AI Search service (provisioning is an Azure-side / infrastructure concern).
/// </para>
/// </remarks>
public class AgenticRetrievalConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether the Azure AI Search agentic-retrieval backend
    /// is active. When <c>true</c>, <c>IHybridRetriever</c> resolves to the knowledge-base
    /// implementation; when <c>false</c> (default), the local hybrid retriever is used.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Azure AI Search service endpoint URL
    /// (e.g., <c>https://myservice.search.windows.net</c>).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the name of the knowledge base to query. The knowledge base, its knowledge
    /// sources, and the underlying index must already be provisioned on the service.
    /// </summary>
    public string KnowledgeBaseName { get; set; } = "agentic-kb";

    /// <summary>
    /// Gets or sets the API key (admin or query key) for the Azure AI Search service. Should be
    /// stored in User Secrets (dev) or Azure Key Vault (prod), never in appsettings.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets a value indicating whether the backend is both enabled and pointed at a
    /// non-empty endpoint. Used to fail soft (return no results) rather than attempt a
    /// doomed network call when enabled but mis-configured.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Endpoint);
}
