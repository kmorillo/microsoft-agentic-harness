using Domain.AI.Evaluation;

namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Writes an <see cref="EvalRunReport"/> to a stream in a specific format
/// (e.g. console-friendly text, JSON for dashboard ingestion, JUnit XML for CI).
/// Implementations are registered as keyed services so the CLI can select them via <c>--out</c>.
/// </summary>
public interface IEvalReporter
{
    /// <summary>The format key by which this reporter is selected (e.g. "console", "json", "junit").</summary>
    string FormatKey { get; }

    /// <summary>
    /// Writes the report.
    /// </summary>
    /// <param name="report">The report to write.</param>
    /// <param name="output">The output stream. Caller owns lifecycle.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task WriteAsync(
        EvalRunReport report,
        Stream output,
        CancellationToken cancellationToken);
}
