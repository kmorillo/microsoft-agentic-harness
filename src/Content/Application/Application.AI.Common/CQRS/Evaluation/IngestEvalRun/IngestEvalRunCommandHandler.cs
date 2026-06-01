using Application.AI.Common.Evaluation.Interfaces;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.AI.Common.CQRS.Evaluation.IngestEvalRun;

/// <summary>
/// Handles <see cref="IngestEvalRunCommand"/> by persisting the supplied
/// <see cref="Domain.AI.Evaluation.EvalRunReport"/> through
/// <see cref="IEvalRunStore.AppendAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// ReceivedAt timestamp is taken from <see cref="TimeProvider"/> (not from the
/// caller) so the dashboard's notion of "when did we receive this?" is owned by
/// the server, not spoofable by the ingest client. Distinct from the report's own
/// <see cref="Domain.AI.Evaluation.EvalRunReport.CompletedAtUtc"/> which the run
/// itself recorded.
/// </para>
/// <para>
/// Store failures other than <see cref="OperationCanceledException"/> are
/// translated into <see cref="Result{T}.Fail(string[])"/>. Successful ingest
/// returns the <see cref="IngestEvalRunResult"/> with <c>Inserted</c> matching
/// the store's idempotency outcome.
/// </para>
/// </remarks>
public sealed class IngestEvalRunCommandHandler
    : IRequestHandler<IngestEvalRunCommand, Result<IngestEvalRunResult>>
{
    private readonly IEvalRunStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestEvalRunCommandHandler> _logger;

    /// <summary>Initializes a new instance.</summary>
    public IngestEvalRunCommandHandler(
        IEvalRunStore store,
        TimeProvider timeProvider,
        ILogger<IngestEvalRunCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IngestEvalRunResult>> Handle(
        IngestEvalRunCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var receivedAtUtc = _timeProvider.GetUtcNow();

        bool inserted;
        try
        {
            inserted = await _store
                .AppendAsync(request.Report, receivedAtUtc, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist eval run {RunId} into the dashboard store.",
                request.Report.RunId);
            return Result<IngestEvalRunResult>.Fail(
                $"Failed to persist eval run: {ex.Message}");
        }

        if (inserted)
        {
            _logger.LogInformation(
                "Ingested eval run {RunId} (started {StartedAt}, verdict {Verdict}, {Cases} cases).",
                request.Report.RunId,
                request.Report.StartedAtUtc,
                request.Report.OverallVerdict,
                request.Report.Results.Count);
        }
        else
        {
            _logger.LogDebug(
                "Eval run {RunId} already present in dashboard store; ingest is a no-op.",
                request.Report.RunId);
        }

        return Result<IngestEvalRunResult>.Success(new IngestEvalRunResult
        {
            RunId = request.Report.RunId,
            Inserted = inserted,
            ReceivedAtUtc = receivedAtUtc,
        });
    }
}
