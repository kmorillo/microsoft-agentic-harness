using FluentAssertions;
using Presentation.AgentHub.AgUi;
using Xunit;

namespace Presentation.AgentHub.Tests.AgUi;

/// <summary>
/// Unit tests for <see cref="PendingToolCallRegistry"/> — the rendezvous between a blocking proxy tool
/// (awaiting inside a run) and the resume endpoint (a separate request). Covers completion, the
/// failure modes that must never leak a pending entry (timeout, cancellation), the unknown-callId path
/// the endpoint reports as 404, and the thread-binding that stops a foreign thread completing a call.
/// </summary>
public sealed class PendingToolCallRegistryTests
{
    private const string T = "thread-1";

    [Fact]
    public async Task RegisterThenComplete_ResolvesWithResult_AndRemovesEntry()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", T, TimeSpan.FromSeconds(5), CancellationToken.None);

        registry.PendingCount.Should().Be(1);
        registry.TryComplete("call-1", T, "the-result").Should().BeTrue();

        (await task).Should().Be("the-result");
        registry.PendingCount.Should().Be(0, "a completed call must not leak a pending entry");
    }

    [Fact]
    public async Task Register_WhenTimeoutElapses_ThrowsTimeout_AndRemovesEntry()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", T, TimeSpan.FromMilliseconds(20), CancellationToken.None);

        var act = async () => await task;
        await act.Should().ThrowAsync<TimeoutException>();
        registry.PendingCount.Should().Be(0, "a timed-out call must not leak a pending entry");
    }

    [Fact]
    public async Task Register_WhenCancelled_ThrowsOperationCanceled_AndRemovesEntry()
    {
        var registry = new PendingToolCallRegistry();
        using var cts = new CancellationTokenSource();
        var task = registry.RegisterAsync("call-1", T, TimeSpan.FromSeconds(30), cts.Token);

        cts.Cancel();

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        registry.PendingCount.Should().Be(0, "a cancelled call must not leak a pending entry");
    }

    [Fact]
    public async Task TryFail_FaultsTheAwaitingTask()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", T, TimeSpan.FromSeconds(5), CancellationToken.None);

        registry.TryFail("call-1", T, "client error").Should().BeTrue();

        var act = async () => await task;
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("client error");
        registry.PendingCount.Should().Be(0);
    }

    [Fact]
    public void TryComplete_UnknownCallId_ReturnsFalse()
    {
        var registry = new PendingToolCallRegistry();
        registry.TryComplete("never-registered", T, "x").Should().BeFalse();
    }

    [Fact]
    public async Task TryComplete_ForeignThread_ReturnsFalse_AndLeavesCallPendingForTheOwner()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", T, TimeSpan.FromSeconds(5), CancellationToken.None);

        // A caller who owns a different thread cannot complete this call, even with the right callId.
        registry.TryComplete("call-1", "other-thread", "injected").Should().BeFalse();
        registry.PendingCount.Should().Be(1, "the foreign completion must leave the call intact");

        // The real owner still completes it.
        registry.TryComplete("call-1", T, "owned").Should().BeTrue();
        (await task).Should().Be("owned");
    }

    [Fact]
    public async Task TryComplete_AfterAlreadyCompleted_ReturnsFalse()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", T, TimeSpan.FromSeconds(5), CancellationToken.None);

        registry.TryComplete("call-1", T, "first").Should().BeTrue();
        await task;

        registry.TryComplete("call-1", T, "second").Should().BeFalse("the call was already completed and removed");
    }

    [Fact]
    public async Task Register_DuplicateCallId_Throws()
    {
        var registry = new PendingToolCallRegistry();
        var first = registry.RegisterAsync("call-1", T, TimeSpan.FromSeconds(5), CancellationToken.None);

        var act = () => registry.RegisterAsync("call-1", T, TimeSpan.FromSeconds(5), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Drain the first registration so it doesn't surface as an unobserved timeout later.
        registry.TryComplete("call-1", T, "done").Should().BeTrue();
        await first;
    }
}
