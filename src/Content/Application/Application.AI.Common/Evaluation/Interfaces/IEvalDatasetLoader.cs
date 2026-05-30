using Domain.AI.Evaluation;

namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Loads an <see cref="EvalDataset"/> from a file path. Implementations are registered
/// per format (e.g. YAML, JSON) and selected by file extension.
/// </summary>
public interface IEvalDatasetLoader
{
    /// <summary>
    /// The file extensions this loader handles, without the leading dot
    /// (e.g. <c>["yaml", "yml"]</c>, <c>["json"]</c>). Matching is case-insensitive.
    /// </summary>
    /// <remarks>
    /// A loader may report multiple equivalent spellings (yaml + yml, json + jsonc).
    /// The dispatcher (<c>RunEvalSuiteCommandHandler</c>) indexes each spelling separately.
    /// </remarks>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Loads the dataset from the given path.
    /// </summary>
    /// <param name="path">Absolute or working-directory-relative file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The loaded dataset.</returns>
    /// <exception cref="FileNotFoundException">The path does not exist.</exception>
    /// <exception cref="InvalidDataException">The file contents cannot be parsed.</exception>
    Task<EvalDataset> LoadAsync(string path, CancellationToken cancellationToken);
}
