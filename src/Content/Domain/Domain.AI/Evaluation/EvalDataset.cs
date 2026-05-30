namespace Domain.AI.Evaluation;

/// <summary>
/// A named, versioned collection of <see cref="EvalCase"/>s loaded from disk.
/// </summary>
/// <remarks>
/// Datasets are typically authored as YAML files and version-controlled. The
/// <see cref="Version"/> field allows runners to surface dataset drift in reports
/// when the same logical dataset has evolved over time.
/// </remarks>
public sealed record EvalDataset
{
    /// <summary>The dataset name (e.g. "governance-sanitization"). Derived from the file path if not declared.</summary>
    public required string Name { get; init; }

    /// <summary>Dataset version string (free-form; semver recommended).</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>Optional human-readable description of the dataset's purpose.</summary>
    public string? Description { get; init; }

    /// <summary>The source file path the dataset was loaded from, when available.</summary>
    public string? SourcePath { get; init; }

    /// <summary>The cases in the dataset. Always non-null but may be empty (a degenerate dataset).</summary>
    public required IReadOnlyList<EvalCase> Cases { get; init; }
}
