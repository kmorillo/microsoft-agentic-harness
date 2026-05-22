using Domain.AI.RAG.Models;
using Domain.AI.Routing.Enums;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Coordinates retrieval across multiple sources (vector store, knowledge graph, web)
/// in parallel, merges results, and deduplicates by chunk ID. Source selection is
/// driven by query complexity.
/// </summary>
public interface IMultiSourceOrchestrator
{
    /// <summary>
    /// Retrieves results from all applicable sources based on query complexity,
    /// merges, deduplicates, and returns a unified result list sorted by fused score.
    /// </summary>
    Task<IReadOnlyList<RetrievalResult>> RetrieveFromAllSourcesAsync(
        string query,
        int topK,
        TaskComplexity complexity,
        CancellationToken cancellationToken = default);
}
