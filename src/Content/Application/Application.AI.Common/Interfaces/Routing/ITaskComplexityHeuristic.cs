// src/Content/Application/Application.AI.Common/Interfaces/Routing/ITaskComplexityHeuristic.cs
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// Fast, zero-cost heuristic classifier for task complexity.
/// Returns null when confidence is below threshold, triggering LLM fallback.
/// </summary>
public interface ITaskComplexityHeuristic
{
    /// <summary>
    /// Classifies the task complexity based on message signals (length, keywords, tool count, etc.).
    /// Returns null if the heuristic is not confident enough.
    /// </summary>
    TaskComplexityAssessment? Classify(AgentTurnContext context);
}
