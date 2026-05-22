using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config;
using Domain.Common.Config.AI.RAG;
using Domain.Common.Config.AI.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.Orchestration;

/// <summary>
/// Determines the retrieval execution path based on query complexity classification.
/// When confidence is below threshold, falls back to Moderate (full pipeline) for safety.
/// When routing is disabled, always returns full pipeline parameters.
/// </summary>
public sealed class RetrievalDecisionGate : IRetrievalDecisionGate
{
    private readonly IOptionsMonitor<AppConfig> _config;
    private readonly ILogger<RetrievalDecisionGate> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="RetrievalDecisionGate"/>.
    /// </summary>
    /// <param name="config">Application configuration monitor.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RetrievalDecisionGate(
        IOptionsMonitor<AppConfig> config,
        ILogger<RetrievalDecisionGate> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public RetrievalDecision Decide(TaskComplexityAssessment classification, int? requestedTopK = null)
    {
        var config = _config.CurrentValue;
        var ragConfig = config.AI.Rag;
        var routingDefaults = config.AI.ModelRouting.RetrievalDefaults;
        var routingEnabled = config.AI.ModelRouting.Enabled;

        if (!routingEnabled)
            return CreateFullPipelineDecision(ragConfig, requestedTopK);

        var effectiveComplexity = classification.Confidence < routingDefaults.ConfidenceThreshold
            ? TaskComplexity.Moderate
            : classification.Complexity;

        if (effectiveComplexity != classification.Complexity)
        {
            _logger.LogDebug(
                "Complexity downgraded from {Original} to Moderate due to low confidence {Confidence:F2}",
                classification.Complexity,
                classification.Confidence);
        }

        return effectiveComplexity switch
        {
            TaskComplexity.Trivial => new RetrievalDecision
            {
                SkipRetrieval = true,
                TopK = 0,
                UseReranking = false,
                UseCragEvaluation = false,
                Complexity = TaskComplexity.Trivial,
            },
            TaskComplexity.Simple => new RetrievalDecision
            {
                SkipRetrieval = false,
                TopK = requestedTopK ?? routingDefaults.SimpleTopK,
                UseReranking = !routingDefaults.SkipRerankForSimple,
                UseCragEvaluation = !routingDefaults.SkipCragForSimple,
                Complexity = TaskComplexity.Simple,
            },
            TaskComplexity.Complex => new RetrievalDecision
            {
                SkipRetrieval = false,
                TopK = requestedTopK ?? routingDefaults.ComplexTopK,
                UseReranking = true,
                UseCragEvaluation = true,
                Complexity = TaskComplexity.Complex,
            },
            _ => CreateFullPipelineDecision(ragConfig, requestedTopK),
        };
    }

    private static RetrievalDecision CreateFullPipelineDecision(RagConfig ragConfig, int? requestedTopK) =>
        new()
        {
            SkipRetrieval = false,
            TopK = requestedTopK ?? ragConfig.Retrieval.TopK,
            UseReranking = true,
            UseCragEvaluation = true,
            Complexity = TaskComplexity.Moderate,
        };
}
