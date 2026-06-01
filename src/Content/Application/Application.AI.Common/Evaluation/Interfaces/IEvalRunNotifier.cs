using Application.AI.Common.Evaluation.Models;

namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Broadcasts an "eval run completed" signal to interested clients (dashboard UI)
/// after a successful ingest. Implementation is host-specific: the dashboard host
/// publishes via SignalR; the CLI host uses a no-op since no clients are attached.
/// </summary>
/// <remarks>
/// <para>
/// Called by <see cref="CQRS.Evaluation.IngestEvalRun.IngestEvalRunCommandHandler"/>
/// only when a NEW row was written. Idempotent re-ingests do NOT trigger a
/// notification — re-runs would otherwise flap UI state across replays.
/// </para>
/// <para>
/// Implementations MUST be safe for fire-and-forget invocation: failures should
/// be logged, not propagated. A dropped notification is acceptable; a corrupted
/// handler result because of a SignalR transport hiccup is not.
/// </para>
/// </remarks>
public interface IEvalRunNotifier
{
    /// <summary>
    /// Notifies subscribers that <paramref name="runSummary"/> has been ingested
    /// for the first time.
    /// </summary>
    Task NotifyRunCompletedAsync(EvalRunSummary runSummary, CancellationToken cancellationToken);
}
