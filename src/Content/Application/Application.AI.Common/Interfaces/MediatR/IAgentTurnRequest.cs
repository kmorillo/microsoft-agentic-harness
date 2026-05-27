namespace Application.AI.Common.Interfaces.MediatR;

/// <summary>
/// Marker interface for requests representing a single agent conversation turn.
/// Extends <see cref="IAgentScopedRequest"/> with the user's message,
/// enabling pipeline behaviors such as <c>KnowledgeExtractionBehavior</c>
/// to operate without a direct dependency on <c>Application.Core</c>.
/// </summary>
public interface IAgentTurnRequest : IAgentScopedRequest
{
    /// <summary>Gets the user's message for this turn.</summary>
    string UserMessage { get; }
}
