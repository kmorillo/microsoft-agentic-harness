using System.Collections.Concurrent;

namespace Presentation.AgentHub.AgUi;

/// <summary>
/// Tracks in-flight client round-trip tool calls so a server-side blocking proxy tool can
/// park mid-run awaiting a result that arrives out-of-band via <c>POST /ag-ui/tool-result</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each pending call is keyed by a globally-unique <c>callId</c> and backed by a
/// <see cref="TaskCompletionSource{TResult}"/>. The proxy tool calls <see cref="RegisterAsync"/>
/// and awaits the returned task; the resume endpoint calls <see cref="TryComplete"/> with the
/// browser-supplied result, which unblocks the awaiting tool and resumes the same agent run.
/// </para>
/// <para>
/// <strong>Lifetime safety:</strong> every registration self-removes from the backing map on
/// completion, timeout, or cancellation, so no <see cref="TaskCompletionSource{TResult}"/> is
/// leaked when a client disconnects or never responds. Registered as a singleton — the resume
/// endpoint (a different HTTP request) and the tool (running inside the run) must share one map.
/// </para>
/// </remarks>
public sealed class PendingToolCallRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a pending tool call and returns a task that completes when the client posts a
    /// result (<see cref="TryComplete"/>), faults when the client reports a failure
    /// (<see cref="TryFail"/>), or throws when the <paramref name="timeout"/> elapses or
    /// <paramref name="cancellationToken"/> is cancelled. The registration is removed from the
    /// backing map in all cases.
    /// </summary>
    /// <param name="callId">Globally-unique identifier for the call. Must not already be pending.</param>
    /// <param name="timeout">Maximum time to await the client result before giving up.</param>
    /// <param name="cancellationToken">Cancelled when the owning run is cancelled (client disconnect).</param>
    /// <returns>The client-supplied result string.</returns>
    /// <exception cref="InvalidOperationException">A call with <paramref name="callId"/> is already pending.</exception>
    /// <exception cref="TimeoutException">The timeout elapsed before a result arrived.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled before a result arrived.</exception>
    public Task<string> RegisterAsync(string callId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        // RunContinuationsAsynchronously prevents the resume endpoint's thread from inlining the
        // (potentially long) agent-run continuation when it calls TrySetResult.
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(callId, tcs))
            throw new InvalidOperationException($"A tool call with id '{callId}' is already pending.");

        return AwaitWithCleanupAsync(callId, tcs.Task, timeout, cancellationToken);
    }

    /// <summary>
    /// Completes a pending call with the client-supplied <paramref name="result"/>, unblocking the
    /// awaiting tool. Returns <c>false</c> when no call with <paramref name="callId"/> is pending
    /// (already completed, timed out, or never registered) so the caller can report "unknown call".
    /// </summary>
    public bool TryComplete(string callId, string result)
    {
        if (_pending.TryRemove(callId, out var tcs))
            return tcs.TrySetResult(result);
        return false;
    }

    /// <summary>
    /// Faults a pending call so the awaiting tool observes a failure rather than a result.
    /// Returns <c>false</c> when no call with <paramref name="callId"/> is pending.
    /// </summary>
    public bool TryFail(string callId, string error)
    {
        if (_pending.TryRemove(callId, out var tcs))
            return tcs.TrySetException(new InvalidOperationException(error));
        return false;
    }

    /// <summary>Gets the number of currently-pending calls. Exposed for diagnostics and leak assertions in tests.</summary>
    public int PendingCount => _pending.Count;

    private async Task<string> AwaitWithCleanupAsync(
        string callId, Task<string> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Idempotent: TryComplete/TryFail may have already removed it; this covers the
            // timeout and cancellation paths where no result ever arrived.
            _pending.TryRemove(callId, out _);
        }
    }
}
