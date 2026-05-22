namespace Domain.AI.Routing.Enums;

/// <summary>
/// Classifies the complexity of a task for model tier selection.
/// Ordered from least to most complex — tier escalation moves upward.
/// </summary>
public enum TaskComplexity
{
    /// <summary>Parametric knowledge, simple lookup, greeting, acknowledgment.</summary>
    Trivial = 0,

    /// <summary>Single-step reasoning, basic tool use, straightforward Q&amp;A.</summary>
    Simple = 1,

    /// <summary>Multi-step reasoning, multiple tools, synthesis, comparison.</summary>
    Moderate = 2,

    /// <summary>Deep reasoning, multi-hop, code generation, planning, architectural decisions.</summary>
    Complex = 3
}
