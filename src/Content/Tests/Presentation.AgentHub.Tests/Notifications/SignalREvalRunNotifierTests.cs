using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Presentation.AgentHub.Hubs;
using Presentation.AgentHub.Notifications;
using Xunit;

namespace Presentation.AgentHub.Tests.Notifications;

/// <summary>
/// SignalR contract tests. The JS dashboard subscribes with
/// <c>connection.on("EvalRunCompleted", payload =&gt; payload.runId)</c> so the
/// property names below are part of the wire contract — renaming any property
/// here silently breaks the dashboard. These tests pin the contract.
/// </summary>
public sealed class SignalREvalRunNotifierTests
{
    private readonly Mock<IHubContext<AgentTelemetryHub>> _hub = new();
    private readonly Mock<IClientProxy> _client = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly SignalREvalRunNotifier _sut;

    public SignalREvalRunNotifierTests()
    {
        _hubClients.Setup(h => h.Group(It.IsAny<string>())).Returns(_client.Object);
        _hub.SetupGet(h => h.Clients).Returns(_hubClients.Object);
        _sut = new SignalREvalRunNotifier(_hub.Object, NullLogger<SignalREvalRunNotifier>.Instance);
    }

    private static EvalRunSummary NewSummary() => new()
    {
        RunId = "run-42",
        StartedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
        CompletedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 1, 30, TimeSpan.Zero),
        Duration = TimeSpan.FromSeconds(90),
        PassedCount = 7,
        FailedCount = 1,
        WarnedCount = 0,
        ErroredCount = 0,
        TotalCostUsd = 0.42m,
        Repeats = 1,
        OverallVerdict = Verdict.Fail,
        ReceivedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 2, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task Broadcasts_to_eval_dashboard_group_with_correct_event_name()
    {
        await _sut.NotifyRunCompletedAsync(NewSummary(), CancellationToken.None);

        _hubClients.Verify(h => h.Group(AgentTelemetryHub.EvalDashboardGroup), Times.Once);
        _client.Verify(c => c.SendCoreAsync(
            AgentTelemetryHub.EventEvalRunCompleted,
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Payload_property_names_are_pinned_for_JS_client_contract()
    {
        object[]? capturedArgs = null;
        _client.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args)
            .Returns(Task.CompletedTask);

        await _sut.NotifyRunCompletedAsync(NewSummary(), CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Should().HaveCount(1);
        var payload = capturedArgs[0];

        // Reflect on the anonymous payload to confirm every expected property name.
        // The JS client deserialises by camelCase property name, so any rename here
        // silently breaks the wire — pin them in test.
        var props = payload.GetType().GetProperties().Select(p => p.Name).ToHashSet();
        props.Should().BeEquivalentTo([
            "runId",
            "startedAtUtc",
            "completedAtUtc",
            "durationMs",
            "passedCount",
            "failedCount",
            "warnedCount",
            "erroredCount",
            "totalCostUsd",
            "repeats",
            "overallVerdict",
            "passRate",
            "receivedAtUtc",
        ]);
    }

    [Fact]
    public async Task Verdict_is_serialised_as_string_not_int()
    {
        object[]? capturedArgs = null;
        _client.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args)
            .Returns(Task.CompletedTask);

        await _sut.NotifyRunCompletedAsync(NewSummary(), CancellationToken.None);

        var payload = capturedArgs![0];
        var verdictValue = payload.GetType().GetProperty("overallVerdict")!.GetValue(payload);
        verdictValue.Should().Be("Fail");
    }

    [Fact]
    public async Task Duration_is_emitted_as_milliseconds_long()
    {
        object[]? capturedArgs = null;
        _client.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args)
            .Returns(Task.CompletedTask);

        await _sut.NotifyRunCompletedAsync(NewSummary(), CancellationToken.None);

        var payload = capturedArgs![0];
        var durationValue = payload.GetType().GetProperty("durationMs")!.GetValue(payload);
        durationValue.Should().BeOfType<long>();
        durationValue.Should().Be(90_000L);
    }

    [Fact]
    public async Task Swallows_transport_failure_to_honour_notifier_contract()
    {
        _client.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub gone"));

        var act = () => _sut.NotifyRunCompletedAsync(NewSummary(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Propagates_OperationCanceledException()
    {
        _client.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _sut.NotifyRunCompletedAsync(NewSummary(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
