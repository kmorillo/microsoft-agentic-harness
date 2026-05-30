using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using Domain.Common;
using MediatR;

namespace Application.Core.CQRS.Evaluation.RunEvalSuite;

/// <summary>
/// Runs one or more evaluation datasets against the harness and returns the aggregated report.
/// </summary>
/// <remarks>
/// <para>
/// Surfaces the eval framework as a first-class CQRS command, callable from anywhere the
/// harness composes <c>IMediator</c>: the EvalRunner CLI, the dashboard, a controller,
/// a scheduled background job, or another agent's workflow.
/// </para>
/// <para>
/// Returns <see cref="Result{T}"/>; failures include missing dataset paths, parse errors,
/// or empty datasets after filtering. Successful runs return the report regardless of
/// per-case verdicts — CI gating is the caller's decision based on <see cref="EvalRunReport.OverallVerdict"/>.
/// </para>
/// </remarks>
public sealed record RunEvalSuiteCommand : IRequest<Result<EvalRunReport>>
{
    /// <summary>One or more dataset file paths to evaluate. Glob patterns are not expanded here — the caller resolves them.</summary>
    public required IReadOnlyList<string> DatasetPaths { get; init; }

    /// <summary>Run options. Sensible defaults if omitted.</summary>
    public EvalRunOptions Options { get; init; } = new();
}
