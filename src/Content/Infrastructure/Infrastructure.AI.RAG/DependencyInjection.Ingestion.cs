using Application.AI.Common.Interfaces.RAG;
using Domain.Common.Config;
using Infrastructure.AI.RAG.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG;

public static partial class DependencyInjection
{
    private static void AddRagIngestion(IServiceCollection services, AppConfig appConfig)
    {
        // Document parsing
        services.AddSingleton<IDocumentParser, MarkdownDocumentParser>();

        // Structure extraction
        services.AddSingleton<IStructureExtractor, MarkdownStructureExtractor>();

        // Chunking strategies — keyed by strategy name
        services.AddKeyedSingleton<IChunkingService>("structure_aware", (sp, _) =>
            new StructureAwareChunker(sp.GetRequiredService<IOptionsMonitor<AppConfig>>()));
        services.AddKeyedSingleton<IChunkingService>("fixed_size", (sp, _) =>
            new FixedSizeChunker(sp.GetRequiredService<IOptionsMonitor<AppConfig>>()));
        services.AddKeyedSingleton<IChunkingService>("semantic", (sp, _) =>
            new SemanticChunker(
                sp.GetRequiredService<IEmbeddingService>(),
                sp.GetRequiredService<IOptionsMonitor<AppConfig>>()));

        // Default chunking service (resolve based on config)
        services.AddSingleton<IChunkingService>(sp =>
        {
            var config = sp.GetRequiredService<IOptionsMonitor<AppConfig>>().CurrentValue;
            var strategy = config.AI.Rag.Ingestion.DefaultStrategy;
            return sp.GetRequiredKeyedService<IChunkingService>(strategy);
        });

        // Strategy resolver
        services.AddSingleton<ChunkingStrategyResolver>();

        // Contextual enrichment
        services.AddSingleton<IContextualEnricher, ContextualChunkEnricher>();

        // RAPTOR summarization
        services.AddSingleton<IRaptorSummarizer, RaptorSummarizer>();

        // Embedding service
        services.AddSingleton<IEmbeddingService, EmbeddingService>();

        // Model router is registered by Infrastructure.AI (unified IModelRouter)
    }
}
