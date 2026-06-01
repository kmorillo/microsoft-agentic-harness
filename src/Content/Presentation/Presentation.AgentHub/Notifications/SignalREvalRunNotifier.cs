using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Presentation.AgentHub.Hubs;

namespace Presentation.AgentHub.Notifications;

/// <summary>
/// Broadcasts <see cref="AgentTelemetryHub.EventEvalRunCompleted"/> to the
/// <see cref="AgentTelemetryHub.EvalDashboardGroup"/> SignalR group when a new
/// eval run is ingested. Subscribers (dashboard UI) refresh their run-history
/// list on the event without polling.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract — payload property names are part of the SignalR wire contract.</b>
/// JS client subscribes via
/// <c>connection.on("EvalRunCompleted", (payload) =&gt; { ... payload.runId ... })</c>;
/// renaming a property here will silently break the dashboard. The shape is pinned
/// in <c>SignalREvalRunNotifierTests</c> — change with care.
/// </para>
/// </remarks>
public sealed class SignalREvalRunNotifier : IEvalRunNotifier
{
    private readonly IHubContext<AgentTelemetryHub> _hub;
    private readonly ILogger<SignalREvalRunNotifier> _logger;

    /// <summary>Initializes a new instance.</summary>
    public SignalREvalRunNotifier(
        IHubContext<AgentTelemetryHub> hub,
        ILogger<SignalREvalRunNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);

        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyRunCompletedAsync(
        EvalRunSummary runSummary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runSummary);

        // Payload property names are camelCase to match the SignalR JSON contract
        // the JS client expects. DO NOT rename these without updating the client.
        var payload = new
        {
            runId = runSummary.RunId,
            startedAtUtc = runSummary.StartedAtUtc,
            completedAtUtc = runSummary.CompletedAtUtc,
            durationMs = (long)runSummary.Duration.TotalMilliseconds,
            passedCount = runSummary.PassedCount,
            failedCount = runSummary.FailedCount,
            warnedCount = runSummary.WarnedCount,
            erroredCount = runSummary.ErroredCount,
            totalCostUsd = runSummary.TotalCostUsd,
            repeats = runSummary.Repeats,
            overallVerdict = runSummary.OverallVerdict.ToString(),
            passRate = runSummary.PassRate,
            receivedAtUtc = runSummary.ReceivedAtUtc,
        };

        try
        {
            await _hub.Clients
                .Group(AgentTelemetryHub.EvalDashboardGroup)
                .SendAsync(AgentTelemetryHub.EventEvalRunCompleted, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast EvalRunCompleted for run {RunId}.",
                runSummary.RunId);
            // Swallow per IEvalRunNotifier contract: notification failures must not
            // corrupt the upstream success.
        }
    }
}
