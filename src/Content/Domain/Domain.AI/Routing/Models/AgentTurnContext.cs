namespace Domain.AI.Routing.Models;

/// <summary>
/// Input context for classifying the complexity of an agent conversation turn.
/// Provides the signals that heuristic and LLM classifiers use.
/// </summary>
public sealed record AgentTurnContext
{
    /// <summary>Conversation identifier for escalation state tracking.</summary>
    public required string ConversationId { get; init; }

    /// <summary>The user's message text for this turn.</summary>
    public required string UserMessage { get; init; }

    /// <summary>Sequential turn number in the conversation (1-based).</summary>
    public required int TurnNumber { get; init; }

    /// <summary>Number of tools available to the agent for this turn.</summary>
    public int AvailableToolCount { get; init; }

    /// <summary>Total conversation depth (may differ from TurnNumber in multi-agent scenarios).</summary>
    public int ConversationDepth { get; init; }

    /// <summary>Names of tools used in recent turns (for tool-chain detection).</summary>
    public IReadOnlyList<string>? RecentToolNames { get; init; }
}
