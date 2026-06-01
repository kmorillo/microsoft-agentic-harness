using Application.AI.Common.Interfaces.Context;
using Domain.AI.Context;

namespace Application.AI.Common.Notifications;

/// <summary>
/// Default <see cref="IContextSnapshotNotifier"/> for hosts without a real-time
/// client transport (CLI, worker). All notifications are dropped silently.
/// </summary>
/// <remarks>
/// Registered as the always-on default in the standard composition root so
/// <see cref="CQRS.Agents.ExecuteAgentTurn.ExecuteAgentTurnCommandHandler"/>
/// can resolve its dependency unconditionally. The AgentHub host overrides
/// with a SignalR-backed implementation that also persists to the
/// observability store. Mirrors <see cref="Evaluation.Notifications.NullEvalRunNotifier"/>.
/// </remarks>
public sealed class NullContextSnapshotNotifier : IContextSnapshotNotifier
{
    /// <inheritdoc />
    public Task NotifyAsync(ContextSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Task.CompletedTask;
    }
}
