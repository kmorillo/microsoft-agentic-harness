using Domain.AI.Routing.Enums;

namespace Domain.AI.Routing.Models;

/// <summary>
/// Result of classifying a task's complexity. Used by the router
/// to select a model tier and by the supervisor for delegation.
/// </summary>
public sealed record TaskComplexityAssessment
{
    /// <summary>Classified complexity level.</summary>
    public required TaskComplexity Complexity { get; init; }

    /// <summary>Classification confidence (0.0–1.0).</summary>
    public required double Confidence { get; init; }

    /// <summary>How this classification was determined.</summary>
    public required ClassificationSource Source { get; init; }

    /// <summary>Optional explanation of why this complexity was chosen.</summary>
    public string? Reasoning { get; init; }

    /// <summary>Whether retrieval should be skipped (true for Trivial tasks).</summary>
    public bool SkipRetrieval => Complexity == TaskComplexity.Trivial;
}
