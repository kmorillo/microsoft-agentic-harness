namespace Application.AI.Common.Interfaces.MediatR;

/// <summary>
/// Marker interface for the result of an agent turn execution.
/// Consumed by <c>KnowledgeExtractionBehavior</c> to inspect turn outcome
/// without introducing a circular project reference to <c>Application.Core</c>.
/// </summary>
public interface IAgentTurnResult
{
    /// <summary>Gets whether the agent turn completed successfully.</summary>
    bool Success { get; }

    /// <summary>Gets the assistant's response text. Empty string on failure.</summary>
    string Response { get; }
}
