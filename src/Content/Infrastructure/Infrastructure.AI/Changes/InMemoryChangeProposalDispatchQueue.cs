using System.Threading.Channels;
using Application.AI.Common.Interfaces.Changes;

namespace Infrastructure.AI.Changes;

/// <summary>
/// In-memory <see cref="IChangeProposalDispatchQueue"/> backed by a single-reader
/// <see cref="Channel{T}"/>. FIFO; unbounded so <see cref="EnqueueAsync"/> never
/// blocks the caller waiting for capacity.
/// </summary>
/// <remarks>
/// <para>
/// Process-local — ids enqueued in one host instance are not visible to
/// another. Crash-loses the queue (matches the in-memory store's restart
/// semantics; <see cref="Infrastructure.AI.Changes.InMemoryChangeProposalStore"/>
/// has the same property and the startup validator forces consumers to
/// explicitly opt in to it outside Development).
/// </para>
/// <para>
/// Single-reader: the channel is configured with
/// <c>SingleReader = true</c> because only the
/// <c>ChangeProposalBackgroundService</c> reads from it. This lets the
/// runtime use a cheaper synchronization primitive than the multi-reader
/// path. If a future deployment fans out to multiple workers, drop the flag.
/// </para>
/// </remarks>
public sealed class InMemoryChangeProposalDispatchQueue : IChangeProposalDispatchQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            // Multiple producers (CQRS handlers across concurrent requests) are
            // expected; do not set SingleWriter.
            AllowSynchronousContinuations = false,
        });

    /// <inheritdoc />
    public ValueTask EnqueueAsync(string proposalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(proposalId);
        return _channel.Writer.WriteAsync(proposalId, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
