namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// Queue of proposal ids awaiting orchestrator dispatch. The
/// <c>SubmitChangeProposalCommand</c> and <c>ApproveChangeProposalCommand</c>
/// handlers enqueue a proposal id after their respective state transition; a
/// background worker (typically <c>ChangeProposalBackgroundService</c>) consumes
/// the queue and drives <c>IChangeProposalOrchestrator.ProcessAsync</c> for each
/// id, so the command handlers don't block the request thread on long-running
/// gates (policy checks, real merges).
/// </summary>
/// <remarks>
/// <para>
/// The default in-memory implementation backs a <c>Channel&lt;string&gt;</c>;
/// proposals enqueued before a host crash are lost (same crash semantics the
/// in-memory store already has — see <c>InMemoryChangeProposalStore</c>).
/// </para>
/// <para>
/// Consumers requiring at-least-once delivery across host restarts swap this
/// for an outbox-backed implementation (e.g. a row in the durable EF Core
/// store, marked Processed only after the orchestrator returns). The queue
/// shape — <c>Enqueue</c> + <c>DequeueAll</c> — matches what such an
/// implementation would expose, so the worker contract stays the same.
/// </para>
/// <para>
/// Idempotency: enqueueing the same proposal id twice causes the orchestrator
/// to call <c>ProcessAsync</c> twice. The orchestrator's resume logic
/// (re-evaluating from the current status, skipping already-Passed gates)
/// makes the second call safe — it picks up where the first left off rather
/// than re-running already-completed work.
/// </para>
/// </remarks>
public interface IChangeProposalDispatchQueue
{
    /// <summary>
    /// Add a proposal id to the queue for asynchronous orchestrator dispatch.
    /// Returns once the id is queued, not once it's processed.
    /// </summary>
    /// <param name="proposalId">The proposal to dispatch.</param>
    /// <param name="cancellationToken">
    /// Cancellation token honored by bounded-queue implementations that may
    /// wait for capacity. Unbounded implementations complete synchronously.
    /// </param>
    ValueTask EnqueueAsync(string proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// Stream proposal ids out of the queue in FIFO order. Yields each id as
    /// it becomes available and completes when the queue is shut down (host
    /// stop, cancellation, or implementation-specific terminate).
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token; cancelling stops the enumeration without throwing
    /// away ids that have already been yielded.
    /// </param>
    /// <returns>An async stream of proposal ids ready for dispatch.</returns>
    IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken);
}
