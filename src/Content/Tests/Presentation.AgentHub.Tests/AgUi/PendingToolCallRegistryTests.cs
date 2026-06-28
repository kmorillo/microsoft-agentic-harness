using FluentAssertions;
using Presentation.AgentHub.AgUi;
using Xunit;

namespace Presentation.AgentHub.Tests.AgUi;

/// <summary>
/// Unit tests for <see cref="PendingToolCallRegistry"/> — the rendezvous between a blocking proxy tool
/// (awaiting inside a run) and the resume endpoint (a separate request). Covers completion, the
/// failure modes that must never leak a pending entry (timeout, cancellation), and the unknown-callId
/// path the endpoint reports as 404.
/// </summary>
public sealed class PendingToolCallRegistryTests
{
    [Fact]
    public async Task RegisterThenComplete_ResolvesWithResult_AndRemovesEntry()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", TimeSpan.FromSeconds(5), CancellationToken.None);

        registry.PendingCount.Should().Be(1);
        registry.TryComplete("call-1", "the-result").Should().BeTrue();

        (await task).Should().Be("the-result");
        registry.PendingCount.Should().Be(0, "a completed call must not leak a pending entry");
    }

    [Fact]
    public async Task Register_WhenTimeoutElapses_ThrowsTimeout_AndRemovesEntry()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", TimeSpan.FromMilliseconds(20), CancellationToken.None);

        var act = async () => await task;
        await act.Should().ThrowAsync<TimeoutException>();
        registry.PendingCount.Should().Be(0, "a timed-out call must not leak a pending entry");
    }

    [Fact]
    public async Task Register_WhenCancelled_ThrowsOperationCanceled_AndRemovesEntry()
    {
        var registry = new PendingToolCallRegistry();
        using var cts = new CancellationTokenSource();
        var task = registry.RegisterAsync("call-1", TimeSpan.FromSeconds(30), cts.Token);

        cts.Cancel();

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        registry.PendingCount.Should().Be(0, "a cancelled call must not leak a pending entry");
    }

    [Fact]
    public async Task TryFail_FaultsTheAwaitingTask()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", TimeSpan.FromSeconds(5), CancellationToken.None);

        registry.TryFail("call-1", "client error").Should().BeTrue();

        var act = async () => await task;
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("client error");
        registry.PendingCount.Should().Be(0);
    }

    [Fact]
    public void TryComplete_UnknownCallId_ReturnsFalse()
    {
        var registry = new PendingToolCallRegistry();
        registry.TryComplete("never-registered", "x").Should().BeFalse();
    }

    [Fact]
    public async Task TryComplete_AfterAlreadyCompleted_ReturnsFalse()
    {
        var registry = new PendingToolCallRegistry();
        var task = registry.RegisterAsync("call-1", TimeSpan.FromSeconds(5), CancellationToken.None);

        registry.TryComplete("call-1", "first").Should().BeTrue();
        await task;

        registry.TryComplete("call-1", "second").Should().BeFalse("the call was already completed and removed");
    }

    [Fact]
    public async Task Register_DuplicateCallId_Throws()
    {
        var registry = new PendingToolCallRegistry();
        var first = registry.RegisterAsync("call-1", TimeSpan.FromSeconds(5), CancellationToken.None);

        var act = () => registry.RegisterAsync("call-1", TimeSpan.FromSeconds(5), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Drain the first registration so it doesn't surface as an unobserved timeout later.
        registry.TryComplete("call-1", "done").Should().BeTrue();
        await first;
    }
}
