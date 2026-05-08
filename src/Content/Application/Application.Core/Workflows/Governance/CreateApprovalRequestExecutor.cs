using Application.AI.Common.Interfaces.Governance;
using Microsoft.Agents.AI.Workflows;

namespace Application.Core.Workflows.Governance;

/// <summary>
/// Transforms a <see cref="GovernanceApprovalInput"/> into a human-readable
/// <see cref="ApprovalRequest"/> and logs the pending approval to the governance audit chain.
/// First step in the governance approval workflow.
/// </summary>
public sealed class CreateApprovalRequestExecutor(
    IGovernanceAuditService auditService)
    : Executor<GovernanceApprovalInput, ApprovalRequest>("CreateApprovalRequest")
{
    /// <summary>
    /// Builds an <see cref="ApprovalRequest"/> from the governance decision metadata
    /// and records the pending approval in the audit trail.
    /// </summary>
    /// <param name="message">The governance input containing the tool call details and initial decision.</param>
    /// <param name="context">The MAF workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="ApprovalRequest"/> ready for human review.</returns>
    public override ValueTask<ApprovalRequest> HandleAsync(
        GovernanceApprovalInput message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var decision = message.InitialDecision;

        auditService.Log(
            message.AgentId,
            message.ToolName,
            $"RequireApproval:Pending|Rule:{decision.MatchedRule ?? "none"}|Policy:{decision.PolicyName ?? "none"}");

        var request = new ApprovalRequest(
            ToolName: message.ToolName,
            AgentId: message.AgentId,
            Description: $"Agent '{message.AgentId}' requests to invoke tool '{message.ToolName}' with arguments: {message.ToolArguments}",
            Risk: decision.MatchedRule ?? decision.Reason,
            Approvers: decision.Approvers ?? [],
            RequestedAt: DateTimeOffset.UtcNow);

        return ValueTask.FromResult(request);
    }
}
