using FluentAssertions;
using Infrastructure.AI.Changes;
using Xunit;

namespace Infrastructure.AI.Tests.Changes;

/// <summary>
/// Tests for <see cref="InMemoryChangeProposalDispatchQueue"/>: enqueue/dequeue
/// FIFO ordering, cancellation behaviour, and argument validation.
/// </summary>
public sealed class InMemoryChangeProposalDispatchQueueTests
{
    [Fact]
    public async Task EnqueueAsync_NullOrEmptyId_Throws()
    {
        var queue = new InMemoryChangeProposalDispatchQueue();

        await queue.Invoking(q => q.EnqueueAsync(null!, CancellationToken.None).AsTask())
            .Should().ThrowAsync<ArgumentException>();
        await queue.Invoking(q => q.EnqueueAsync(string.Empty, CancellationToken.None).AsTask())
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DequeueAllAsync_YieldsIdsInEnqueueOrder()
    {
        var queue = new InMemoryChangeProposalDispatchQueue();
        await queue.EnqueueAsync("p1", CancellationToken.None);
        await queue.EnqueueAsync("p2", CancellationToken.None);
        await queue.EnqueueAsync("p3", CancellationToken.None);

        // Cancel after 3 reads so DequeueAllAsync (which would otherwise wait
        // for more) terminates the enumeration cleanly.
        using var cts = new CancellationTokenSource();
        var received = new List<string>();
        try
        {
            await foreach (var id in queue.DequeueAllAsync(cts.Token))
            {
                received.Add(id);
                if (received.Count == 3)
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException) { /* expected — terminates the loop */ }

        received.Should().Equal("p1", "p2", "p3");
    }

    [Fact]
    public async Task DequeueAllAsync_WhenCancelledBeforeEnqueue_ReturnsNoItems()
    {
        var queue = new InMemoryChangeProposalDispatchQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var received = new List<string>();
        try
        {
            await foreach (var id in queue.DequeueAllAsync(cts.Token))
            {
                received.Add(id);
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        received.Should().BeEmpty();
    }
}
