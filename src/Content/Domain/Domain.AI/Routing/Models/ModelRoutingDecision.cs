using Domain.AI.Routing.Enums;
using Microsoft.Extensions.AI;

namespace Domain.AI.Routing.Models;

/// <summary>
/// Complete routing decision: which model tier was selected, the resolved client,
/// the complexity assessment, and whether escalation was applied.
/// </summary>
public sealed record ModelRoutingDecision
{
    /// <summary>The selected model tier.</summary>
    public required ModelTier SelectedTier { get; init; }

    /// <summary>Resolved IChatClient for the selected tier.</summary>
    public required IChatClient Client { get; init; }

    /// <summary>Assessed task complexity.</summary>
    public required TaskComplexity Complexity { get; init; }

    /// <summary>How the complexity was classified.</summary>
    public required ClassificationSource Source { get; init; }

    /// <summary>Classification confidence (0.0–1.0).</summary>
    public required double Confidence { get; init; }

    /// <summary>Optional reasoning from the classifier.</summary>
    public string? Reasoning { get; init; }

    /// <summary>True if this decision was escalated from a lower tier due to quality signals.</summary>
    public bool WasEscalated { get; init; }
}
