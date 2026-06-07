using Application.AI.Common.Interfaces.Changes;
using Application.AI.Common.Interfaces.Escalation;
using Domain.AI.Changes;
using Domain.AI.Escalation;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Changes.Gates;

/// <summary>
/// Default <see cref="IChangeApprovalRouter"/> — adapts a
/// <see cref="ChangeProposal"/> into the existing <see cref="EscalationRequest"/>
/// shape and queues it via <see cref="IEscalationService"/>. Maps
/// <see cref="BlastRadius"/> to <see cref="RiskLevel"/> and
/// <see cref="EscalationPriority"/> so the existing per-priority
/// approval / notification configuration applies uniformly.
/// </summary>
/// <remarks>
/// <para>
/// Idempotency: the escalation id is derived deterministically from the proposal
/// id plus a per-attempt salt so a Defer-retry of the approval gate doesn't
/// queue a duplicate escalation. The existing escalation service treats
/// the id as primary key, so re-queueing with the same id is a no-op.
/// </para>
/// <para>
/// Approver list pulled from <c>ChangesConfig.DefaultApprovers</c>. If empty,
/// the router throws — silently producing an escalation with no approvers is
/// a misconfiguration that would leave the proposal stuck forever in
/// AwaitingApproval.
/// </para>
/// </remarks>
public sealed class EscalationServiceApprovalRouter : IChangeApprovalRouter
{
    private readonly IEscalationService _escalation;
    private readonly IOptionsMonitor<AppConfig> _config;
    private readonly TimeProvider _time;
    private readonly ILogger<EscalationServiceApprovalRouter> _logger;

    /// <summary>Initializes a new <see cref="EscalationServiceApprovalRouter"/>.</summary>
    public EscalationServiceApprovalRouter(
        IEscalationService escalation,
        IOptionsMonitor<AppConfig> config,
        TimeProvider time,
        ILogger<EscalationServiceApprovalRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(escalation);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _escalation = escalation;
        _config = config;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RouteAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);

        var changesConfig = _config.CurrentValue.AI.Changes;
        var approvers = changesConfig.DefaultApprovers;
        if (approvers.Count == 0)
        {
            throw new InvalidOperationException(
                "EscalationServiceApprovalRouter cannot enqueue: AppConfig.AI.Changes.DefaultApprovers is empty. " +
                "Configure at least one approver or register a custom IChangeApprovalRouter.");
        }

        var request = new EscalationRequest
        {
            EscalationId = DeriveEscalationId(proposal.Id, context.AttemptCount),
            AgentId = proposal.SubmittedBy.Id,
            ToolName = $"change_proposal:{proposal.Target.Kind}",
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["proposal_id"] = proposal.Id,
                ["target"] = proposal.Target.DisplayName,
                ["blast_radius"] = proposal.BlastRadius.ToString(),
                ["diff_edits"] = proposal.Diff.Count.ToString()
            },
            Description = proposal.Summary,
            RiskLevel = MapRiskLevel(proposal.BlastRadius),
            Priority = MapPriority(proposal.BlastRadius),
            Approvers = approvers,
            RequestedAt = _time.GetUtcNow(),
        };

        await _escalation.QueueEscalationAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Queued escalation {EscalationId} for ChangeProposal {ProposalId} (blast {BlastRadius}, attempt {Attempt}).",
            request.EscalationId,
            proposal.Id,
            proposal.BlastRadius,
            context.AttemptCount);
    }

    private static Guid DeriveEscalationId(string proposalId, int attempt)
    {
        // Deterministic GUID v5-style from (proposalId, attempt). We don't use
        // real GUID v5 because the escalation system is happy with any stable
        // Guid; using GetDeterministicGuid keeps the deps tight.
        var seed = $"{proposalId}|attempt:{attempt}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private static RiskLevel MapRiskLevel(BlastRadius radius) => radius switch
    {
        BlastRadius.Trivial => RiskLevel.Low,
        BlastRadius.Low => RiskLevel.Low,
        BlastRadius.Medium => RiskLevel.Medium,
        BlastRadius.High => RiskLevel.High,
        BlastRadius.Critical => RiskLevel.Critical,
        _ => RiskLevel.Medium
    };

    private static EscalationPriority MapPriority(BlastRadius radius) => radius switch
    {
        BlastRadius.Trivial => EscalationPriority.Informational,
        BlastRadius.Low => EscalationPriority.Informational,
        BlastRadius.Medium => EscalationPriority.Blocking,
        BlastRadius.High => EscalationPriority.Blocking,
        BlastRadius.Critical => EscalationPriority.Critical,
        _ => EscalationPriority.Blocking
    };
}
