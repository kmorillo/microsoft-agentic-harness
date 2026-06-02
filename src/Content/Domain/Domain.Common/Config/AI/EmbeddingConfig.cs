namespace Domain.Common.Config.AI;

/// <summary>
/// Optional dedicated configuration for the embedding provider used by RAG and
/// knowledge graph features. Bound from <c>AppConfig:AI:Embedding</c> in
/// appsettings.json.
/// </summary>
/// <remarks>
/// <para>
/// Configure this section when the chat provider selected in
/// <see cref="AgentFrameworkConfig.ClientType"/> does not expose an embeddings
/// API (e.g. <c>Anthropic</c>, <c>AzureAIInference</c>). When left unset, the
/// harness reuses the chat provider's client for embeddings — supported for
/// <c>AzureOpenAI</c> and <c>OpenAI</c>.
/// </para>
/// <para>
/// The deployment name configured here overrides
/// <see cref="RAG.VectorStoreConfig.EmbeddingModel"/> when present, allowing the
/// embedding model and vector index name to be decoupled from each other.
/// </para>
/// </remarks>
public class EmbeddingConfig
{
    /// <summary>
    /// Gets or sets the embedding provider client type. When <c>null</c>, the
    /// embedding provider falls back to the chat provider in
    /// <see cref="AgentFrameworkConfig.ClientType"/>.
    /// </summary>
    public AIAgentFrameworkClientType? ClientType { get; set; }

    /// <summary>
    /// Gets or sets the embedding provider endpoint URL. Required when
    /// <see cref="ClientType"/> is <c>AzureOpenAI</c>.
    /// Store in User Secrets or Key Vault — never in appsettings.json.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the API key for the embedding provider.
    /// Store in User Secrets or Key Vault — never in appsettings.json.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the embedding deployment/model name (e.g.
    /// <c>text-embedding-3-large</c>). When <c>null</c> or empty, the deployment
    /// name from <c>AppConfig:AI:Rag:VectorStore:EmbeddingModel</c> is used.
    /// </summary>
    public string? Deployment { get; set; }

    /// <summary>
    /// Returns <c>true</c> when an explicit embedding provider is configured
    /// (both <see cref="ClientType"/> and <see cref="ApiKey"/> are set).
    /// </summary>
    public bool IsConfigured =>
        ClientType.HasValue && !string.IsNullOrWhiteSpace(ApiKey);
}
