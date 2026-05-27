using Application.AI.Common.Interfaces.Agents;
using Domain.AI.Agents;
using Domain.AI.Governance;
using Domain.AI.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Agents;

public sealed partial class CapabilityMatchSupervisor
{
    private async Task<SupervisorDecisionContext> BuildDecisionContextAsync(
        string taskDescription,
        IReadOnlyList<string> requiredCapabilities,
        AutonomyLevel minimumTier,
        int currentDepth,
        int maxDepth,
        CancellationToken ct)
    {
        var profiles = _profileRegistry.GetAllProfiles();
        var candidates = new List<AgentCandidate>(profiles.Count);

        foreach (var (type, definition) in profiles)
        {
            var tools = _toolResolver.ResolveToolsForSubagent(definition, Array.Empty<AITool>());
            var toolNames = tools.Select(t => t.Name).ToList();
            var autonomy = _tierResolver.Resolve(definition);

            candidates.Add(new AgentCandidate
            {
                AgentId = type.ToString(),
                AgentType = type,
                AutonomyLevel = autonomy,
                AvailableTools = toolNames
            });
        }

        Domain.AI.Routing.Models.TaskComplexityAssessment? complexityAssessment = null;

        if (_modelRouter is not null)
        {
            try
            {
                complexityAssessment = await _modelRouter.AssessTaskComplexityAsync(
                    taskDescription, requiredCapabilities, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Model router complexity assessment failed — proceeding without complexity hint");
            }
        }

        return new SupervisorDecisionContext
        {
            TaskDescription = taskDescription,
            RequiredCapabilities = requiredCapabilities,
            MinimumAutonomyLevel = minimumTier,
            AvailableAgents = candidates,
            CurrentDelegationDepth = currentDepth,
            MaxDelegationDepth = maxDepth,
            ComplexityAssessment = complexityAssessment
        };
    }

    private static DelegationRecord BuildPendingRecord(
        Guid delegationId,
        AgentSelection selection,
        string taskDescription,
        IReadOnlyList<string> requiredCapabilities,
        IReadOnlyList<string>? toolOverrides,
        int currentDepth)
    {
        return new DelegationRecord
        {
            DelegationId = delegationId,
            SupervisorId = SupervisorId,
            DelegateAgentId = selection.SelectedAgent.AgentId,
            DelegateAgentType = selection.SelectedAgent.AgentType,
            TaskDescription = taskDescription,
            RequiredCapabilities = requiredCapabilities,
            ToolOverrides = toolOverrides,
            AutonomyLevel = selection.SelectedAgent.AutonomyLevel,
            State = DelegationState.Pending,
            DelegationDepth = currentDepth,
            StartedAt = DateTimeOffset.UtcNow
        };
    }
}
