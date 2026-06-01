using Domain.AI.Evaluation;
using Domain.Common;
using MediatR;

namespace Application.AI.Common.CQRS.Evaluation.IngestEvalRun;

/// <summary>
/// Persists an <see cref="EvalRunReport"/> into the eval dashboard durable store
/// (Sub-phase 5.4.2). Called by the EvalRunner CLI after a run completes and by
/// the dashboard's ingestion HTTP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent on <see cref="EvalRunReport.RunId"/>: re-ingesting the same report
/// returns success with <see cref="IngestEvalRunResult.Inserted"/> set to
/// <c>false</c>; the original row is not overwritten. Run reports are immutable
/// artifacts — corrections happen by rerunning the suite (yielding a new RunId).
/// </para>
/// <para>
/// Failure modes flow through <see cref="Result{T}"/>:
/// <list type="bullet">
///   <item><description><see cref="Result{T}.ValidationFailure"/> from the validator for null report or missing RunId.</description></item>
///   <item><description><see cref="Result{T}.Fail(string[])"/> when the store throws a transient persistence error.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed record IngestEvalRunCommand : IRequest<Result<IngestEvalRunResult>>
{
    /// <summary>The report to persist. RunId carries idempotency semantics.</summary>
    public required EvalRunReport Report { get; init; }
}
