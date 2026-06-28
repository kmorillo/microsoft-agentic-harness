using Application.AI.Common.Interfaces.Tools;
using FluentAssertions;
using Infrastructure.AI.Tools;
using Xunit;

namespace Infrastructure.AI.Tests.Tools;

/// <summary>
/// Tests for <see cref="DashboardControlTool"/> — the blocking proxy that delegates dashboard actions
/// to the connected client via <see cref="IClientToolBridge"/>. Covers operation validation, the
/// no-client failure, the serialized payload handed to the bridge, and timeout/cancellation handling.
/// </summary>
public sealed class DashboardControlToolTests
{
    [Fact]
    public void Metadata_IsCorrect()
    {
        var sut = new DashboardControlTool(new FakeBridge());
        sut.Name.Should().Be("dashboard_control");
        sut.Description.Should().NotBeNullOrWhiteSpace();
        sut.SupportedOperations.Should().BeEquivalentTo(["get_state", "set_time_range", "navigate", "refresh_data"]);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownOperation_Fails_WithoutCallingBridge()
    {
        var bridge = new FakeBridge();
        var sut = new DashboardControlTool(bridge);

        var result = await sut.ExecuteAsync("explode", new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        bridge.InvokeCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NoClientAttached_Fails()
    {
        var sut = new DashboardControlTool(new FakeBridge { ClientAttached = false });

        var result = await sut.ExecuteAsync("get_state", new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("client");
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_PassesSerializedPayload_AndReturnsBridgeResult()
    {
        var bridge = new FakeBridge { Result = "navigated to /spend" };
        var sut = new DashboardControlTool(bridge);

        var result = await sut.ExecuteAsync("navigate",
            new Dictionary<string, object?> { ["path"] = "/spend" });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("navigated to /spend");
        bridge.LastToolName.Should().Be("dashboard_control");
        bridge.LastArgsJson.Should().Contain("navigate").And.Contain("/spend");
    }

    [Fact]
    public async Task ExecuteAsync_BridgeTimeout_FailsGracefully()
    {
        var sut = new DashboardControlTool(new FakeBridge { Throw = new TimeoutException() });

        var result = await sut.ExecuteAsync("refresh_data", new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        var sut = new DashboardControlTool(new FakeBridge { Throw = new OperationCanceledException() });

        var act = async () => await sut.ExecuteAsync("get_state", new Dictionary<string, object?>());

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a cancelled run must unwind rather than being swallowed as a tool failure");
    }

    private sealed class FakeBridge : IClientToolBridge
    {
        public bool ClientAttached { get; init; } = true;
        public string Result { get; init; } = "ok";
        public Exception? Throw { get; init; }

        public int InvokeCount { get; private set; }
        public string? LastToolName { get; private set; }
        public string? LastArgsJson { get; private set; }

        public bool IsClientAttached => ClientAttached;

        public Task<string> InvokeAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            LastToolName = toolName;
            LastArgsJson = argumentsJson;
            if (Throw is not null)
                return Task.FromException<string>(Throw);
            return Task.FromResult(Result);
        }
    }
}
