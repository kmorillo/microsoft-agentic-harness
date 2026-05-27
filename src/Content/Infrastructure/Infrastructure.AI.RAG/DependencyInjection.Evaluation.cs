using Application.AI.Common.Interfaces.RAG;
using Application.AI.Common.Interfaces.Routing;
using Domain.Common.Config;
using Infrastructure.AI.RAG.Assembly;
using Infrastructure.AI.RAG.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG;

public static partial class DependencyInjection
{
    /// <summary>
    /// Registers Phase 5 quality control services: CRAG evaluation, pointer expansion,
    /// citation tracking, and context assembly.
    /// </summary>
    private static void AddRagEvaluation(IServiceCollection services, AppConfig appConfig)
    {
        // CRAG evaluator — singleton (stateless, uses model router for LLM calls)
        services.AddSingleton<ICragEvaluator, CragEvaluator>();

        // Pointer expander — singleton (stateless, deduplicates per-call via local sets)
        services.AddSingleton<IPointerExpander, PointerChunkExpander>();

        // Context assembler — singleton (creates CitationTracker internally per call)
        services.AddSingleton<IRagContextAssembler, RagContextAssembler>();
    }

    /// <summary>
    /// Registers Phase B answer faithfulness evaluation services.
    /// </summary>
    private static void AddRagFaithfulness(IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<IAnswerFaithfulnessEvaluator, AnswerFaithfulnessEvaluator>();
    }

    /// <summary>
    /// Registers the Ragas-inspired <see cref="IRetrievalQualityEvaluator"/> for evaluating
    /// retrieval quality via LLM judges. Used by CI/CD quality gate tests and runtime
    /// quality monitoring.
    /// </summary>
    private static void AddRagQualityGates(IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<IRetrievalQualityEvaluator>(sp =>
            new RetrievalQualityEvaluator(
                sp.GetRequiredService<IModelRouter>(),
                sp.GetRequiredService<ILogger<RetrievalQualityEvaluator>>()));
    }
}
