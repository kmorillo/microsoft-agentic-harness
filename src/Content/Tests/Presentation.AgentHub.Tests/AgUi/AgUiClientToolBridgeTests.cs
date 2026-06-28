using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Presentation.AgentHub.AgUi;
using Presentation.AgentHub.Config;
using Xunit;

namespace Presentation.AgentHub.Tests.AgUi;

/// <summary>
/// Unit tests for <see cref="AgUiClientToolBridge"/> — the mid-run blocking proxy. Verifies it emits the
/// <c>TOOL_CALL_START</c>/<c>ARGS</c>/<c>END</c> sequence (sharing one callId), parks until the registry
/// is completed out-of-band, returns the client's result, and fails fast when no client is attached.
/// </summary>
public sealed class AgUiClientToolBridgeTests
{
    private static IOptionsMonitor<AgentHubConfig> Options(int timeoutSeconds = 30)
    {
        var mock = new Mock<IOptionsMonitor<AgentHubConfig>>();
        mock.Setup(m => m.CurrentValue).Returns(new AgentHubConfig { ClientToolTimeoutSeconds = timeoutSeconds });
        return mock.Object;
    }

    [Fact]
    public async Task InvokeAsync_EmitsToolCallSequence_BlocksUntilCompleted_ThenReturnsResult()
    {
        var writer = new CapturingEventWriter();
        var accessor = new AgUiEventWriterAccessor { Writer = writer };
        var registry = new PendingToolCallRegistry();
        var bridge = new AgUiClientToolBridge(accessor, registry, Options());

        var invokeTask = bridge.InvokeAsync("dashboard_control", "{\"operation\":\"navigate\"}");

        // The three events must be emitted before the call resolves, all sharing one callId.
        await WaitForAsync(() => writer.Events.Count >= 3);
        invokeTask.IsCompleted.Should().BeFalse("the bridge must block awaiting the client result");

        var start = writer.Events[0].Should().BeOfType<ToolCallStartEvent>().Subject;
        var args = writer.Events[1].Should().BeOfType<ToolCallArgsEvent>().Subject;
        var end = writer.Events[2].Should().BeOfType<ToolCallEndEvent>().Subject;

        start.ToolCallName.Should().Be("dashboard_control");
        args.Delta.Should().Contain("navigate");
        start.ToolCallId.Should().Be(args.ToolCallId).And.Be(end.ToolCallId);

        // Resume out-of-band, exactly like the resume endpoint completing the registry.
        registry.TryComplete(start.ToolCallId, "navigated").Should().BeTrue();

        (await invokeTask.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("navigated");
    }

    [Fact]
    public async Task InvokeAsync_NoClientAttached_Throws()
    {
        var accessor = new AgUiEventWriterAccessor { Writer = null };
        var bridge = new AgUiClientToolBridge(accessor, new PendingToolCallRegistry(), Options());

        bridge.IsClientAttached.Should().BeFalse();

        var act = async () => await bridge.InvokeAsync("dashboard_control", "{}");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void IsClientAttached_ReflectsAmbientWriter()
    {
        var accessor = new AgUiEventWriterAccessor();
        var bridge = new AgUiClientToolBridge(accessor, new PendingToolCallRegistry(), Options());

        bridge.IsClientAttached.Should().BeFalse();
        accessor.Writer = new CapturingEventWriter();
        bridge.IsClientAttached.Should().BeTrue();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().Should().BeTrue("the expected state was not reached within the timeout");
    }

    /// <summary>An <see cref="IAgUiEventWriter"/> that records every event written, in order.</summary>
    private sealed class CapturingEventWriter : IAgUiEventWriter
    {
        private readonly List<AgUiEvent> _events = [];
        public IReadOnlyList<AgUiEvent> Events => _events;

        public Task WriteAsync(AgUiEvent evt, CancellationToken ct = default)
        {
            _events.Add(evt);
            return Task.CompletedTask;
        }
    }
}
