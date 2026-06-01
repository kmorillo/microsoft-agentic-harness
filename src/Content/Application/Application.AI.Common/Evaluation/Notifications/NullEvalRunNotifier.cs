using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;

namespace Application.AI.Common.Evaluation.Notifications;

/// <summary>
/// Default <see cref="IEvalRunNotifier"/> for hosts without a real-time client
/// transport (CLI, worker). All notifications are dropped silently.
/// </summary>
/// <remarks>
/// Registered as the always-on default in the standard composition root so
/// <see cref="CQRS.Evaluation.IngestEvalRun.IngestEvalRunCommandHandler"/> can
/// resolve its dependency unconditionally. The AgentHub host overrides with
/// a SignalR-backed implementation.
/// </remarks>
public sealed class NullEvalRunNotifier : IEvalRunNotifier
{
    /// <inheritdoc />
    public Task NotifyRunCompletedAsync(
        EvalRunSummary runSummary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runSummary);
        return Task.CompletedTask;
    }
}
