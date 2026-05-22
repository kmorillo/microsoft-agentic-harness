// src/Content/Application/Application.AI.Common/Interfaces/Routing/IModelRouter.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// Unified model router that handles all model selection decisions:
/// agent turn routing, RAG operation routing, and supervisor delegation advisory.
/// Replaces <c>IRagModelRouter</c>.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Routes an agent conversation turn to the appropriate model tier.
    /// Classifies complexity via heuristic (fast) then LLM (fallback), applies escalation.
    /// </summary>
    Task<ModelRoutingDecision> RouteAgentTurnAsync(
        AgentTurnContext turnContext,
        CancellationToken ct = default);

    /// <summary>
    /// Routes a named operation (e.g., RAG pipeline step) to its configured model tier.
    /// Uses <c>ModelRoutingConfig.OperationOverrides</c> for tier mapping.
    /// </summary>
    Task<ModelRoutingDecision> RouteOperationAsync(
        string operationName,
        CancellationToken ct = default);

    /// <summary>
    /// Assesses task complexity for supervisor delegation decisions.
    /// Advisory only — does not return an IChatClient.
    /// </summary>
    Task<TaskComplexityAssessment> AssessTaskComplexityAsync(
        string taskDescription,
        IReadOnlyList<string> requiredCapabilities,
        CancellationToken ct = default);

    /// <summary>
    /// Reports turn outcome for auto-escalation tracking.
    /// Call after each agent turn completes.
    /// </summary>
    void ReportTurnOutcome(string conversationId, TurnOutcome outcome);
}
