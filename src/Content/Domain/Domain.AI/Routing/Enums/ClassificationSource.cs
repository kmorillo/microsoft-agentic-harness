namespace Domain.AI.Routing.Enums;

/// <summary>Identifies how a complexity classification was determined.</summary>
public enum ClassificationSource
{
    /// <summary>Fast heuristic rules (zero LLM cost).</summary>
    Heuristic,

    /// <summary>LLM-based few-shot classification (fallback for ambiguous cases).</summary>
    LlmClassifier
}
