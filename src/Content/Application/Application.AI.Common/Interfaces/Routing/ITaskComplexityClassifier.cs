// src/Content/Application/Application.AI.Common/Interfaces/Routing/ITaskComplexityClassifier.cs
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// LLM-based few-shot complexity classifier. Used as fallback when the heuristic
/// is not confident. Uses the economy-tier model for classification.
/// Replaces <c>IQueryComplexityClassifier</c>.
/// </summary>
public interface ITaskComplexityClassifier
{
    /// <summary>
    /// Classifies task complexity using an LLM with few-shot examples.
    /// Adds ~200ms latency. Falls back to Moderate on any failure.
    /// </summary>
    Task<TaskComplexityAssessment> ClassifyAsync(
        AgentTurnContext context,
        CancellationToken ct = default);
}
