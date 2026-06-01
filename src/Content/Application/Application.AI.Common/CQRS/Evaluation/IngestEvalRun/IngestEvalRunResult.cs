namespace Application.AI.Common.CQRS.Evaluation.IngestEvalRun;

/// <summary>
/// Output of <see cref="IngestEvalRunCommand"/>: the RunId that was ingested
/// (or already-present) plus a flag indicating whether a new row was written.
/// </summary>
/// <remarks>
/// <para>
/// Idempotency is enforced at the store boundary: re-ingesting the same RunId is a
/// no-op. <see cref="Inserted"/> distinguishes a first-write (<c>true</c>) from
/// a redundant re-ingest (<c>false</c>) so the caller can decide whether to push
/// a real-time notification or to suppress it.
/// </para>
/// </remarks>
public sealed record IngestEvalRunResult
{
    /// <summary>The natural identifier of the ingested run.</summary>
    public required string RunId { get; init; }

    /// <summary>
    /// <c>true</c> when this call wrote a new row; <c>false</c> when the run was
    /// already present (idempotent re-ingest).
    /// </summary>
    public required bool Inserted { get; init; }

    /// <summary>UTC timestamp the dashboard recorded as the ingest moment.</summary>
    public required DateTimeOffset ReceivedAtUtc { get; init; }
}
