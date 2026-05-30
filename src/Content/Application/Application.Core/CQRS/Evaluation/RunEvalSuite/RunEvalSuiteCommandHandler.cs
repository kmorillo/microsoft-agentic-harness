using Application.AI.Common.Evaluation.Interfaces;
using Domain.AI.Evaluation;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Core.CQRS.Evaluation.RunEvalSuite;

/// <summary>
/// Handles <see cref="RunEvalSuiteCommand"/> by dispatching each declared dataset path
/// to its extension-matched <see cref="IEvalDatasetLoader"/>, then feeding the resulting
/// datasets through <see cref="IEvalRunner"/> with the supplied options.
/// </summary>
/// <remarks>
/// Translates expected failure modes (missing file, no matching loader, malformed dataset)
/// into <see cref="Result{T}"/> failures rather than bubbling exceptions through MediatR.
/// </remarks>
public sealed class RunEvalSuiteCommandHandler : IRequestHandler<RunEvalSuiteCommand, Result<EvalRunReport>>
{
    private readonly IReadOnlyDictionary<string, IEvalDatasetLoader> _loadersByExtension;
    private readonly IEvalRunner _runner;
    private readonly ILogger<RunEvalSuiteCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunEvalSuiteCommandHandler"/> class.
    /// </summary>
    /// <param name="loaders">All registered dataset loaders. Each loader's reported extensions are indexed individually.</param>
    /// <param name="runner">The configured eval runner.</param>
    /// <param name="logger">Logger for orchestration diagnostics.</param>
    public RunEvalSuiteCommandHandler(
        IEnumerable<IEvalDatasetLoader> loaders,
        IEvalRunner runner,
        ILogger<RunEvalSuiteCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(loaders);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(logger);

        var index = new Dictionary<string, IEvalDatasetLoader>(StringComparer.OrdinalIgnoreCase);
        foreach (var loader in loaders)
        {
            foreach (var ext in loader.Extensions ?? [])
            {
                var key = ext.TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) continue;

                if (index.TryGetValue(key, out var existing) && !ReferenceEquals(existing, loader))
                {
                    logger.LogWarning(
                        "Dataset-loader registration conflict on extension '{Extension}': {New} shadows {Existing}.",
                        key, loader.GetType().Name, existing.GetType().Name);
                }

                index[key] = loader;
            }
        }
        _loadersByExtension = index;
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<EvalRunReport>> Handle(RunEvalSuiteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defensive guard: validator should catch empty paths, but template consumers may
        // forget to register RequestValidationBehavior (flagged as a common mistake in CLAUDE.md).
        if (request.DatasetPaths is null || request.DatasetPaths.Count == 0)
        {
            return Result<EvalRunReport>.ValidationFailure(["At least one dataset path is required."]);
        }

        var datasets = new List<EvalDataset>(request.DatasetPaths.Count);

        foreach (var path in request.DatasetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
            {
                return Result<EvalRunReport>.ValidationFailure(["Dataset paths must not be empty strings."]);
            }

            if (!File.Exists(path))
            {
                _logger.LogWarning("Eval dataset not found at {Path}", path);
                return Result<EvalRunReport>.NotFound($"Dataset file not found: {path}");
            }

            var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (!_loadersByExtension.TryGetValue(extension, out var loader))
            {
                _logger.LogWarning("No dataset loader registered for extension {Extension} ({Path})", extension, path);
                return Result<EvalRunReport>.Fail(
                    $"No dataset loader registered for extension '{extension}' (file: {path}).");
            }

            try
            {
                var dataset = await loader.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                datasets.Add(dataset);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "Loader reported missing file for {Path}", path);
                return Result<EvalRunReport>.NotFound($"Dataset file not found: {path}");
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Failed to parse dataset {Path}", path);
                return Result<EvalRunReport>.Fail($"Failed to parse dataset {path}: {ex.Message}");
            }
        }

        var report = await _runner.RunAsync(datasets, request.Options, cancellationToken).ConfigureAwait(false);
        return Result<EvalRunReport>.Success(report);
    }
}
