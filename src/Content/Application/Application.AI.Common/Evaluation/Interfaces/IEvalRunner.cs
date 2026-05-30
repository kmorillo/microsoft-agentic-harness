using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;

namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Executes one or more evaluation datasets against the harness via <c>IAgentInvoker</c>
/// and produces an <see cref="EvalRunReport"/>.
/// </summary>
/// <remarks>
/// Two implementations are provided: a sequential runner for deterministic local development
/// and a parallel runner (bounded by <c>SemaphoreSlim</c>) for CI throughput. Both produce
/// identical reports for the same input.
/// </remarks>
public interface IEvalRunner
{
    /// <summary>
    /// Runs the given datasets through the harness and scores each case.
    /// </summary>
    /// <param name="datasets">The datasets to evaluate.</param>
    /// <param name="options">Run options (repeats, parallelism, filters, threshold).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aggregated run report.</returns>
    Task<EvalRunReport> RunAsync(
        IReadOnlyList<EvalDataset> datasets,
        EvalRunOptions options,
        CancellationToken cancellationToken);
}
