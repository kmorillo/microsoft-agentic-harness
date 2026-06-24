namespace Presentation.AgentHub.DTOs;

/// <summary>
/// Result of a single agent turn dispatched by <see cref="Interfaces.IConversationOrchestrator"/>.
/// The transport layer (hub, REST endpoint) uses this to emit the appropriate client events.
/// </summary>
public sealed record TurnOutcome
{
    public required bool Success { get; init; }
    public string? Response { get; init; }
    public Guid AssistantMessageId { get; init; }
    public int FinalTurnNumber { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// When non-null, indicates the conversation was truncated before dispatch (retry/edit).
    /// The transport should emit a truncation event with this count before streaming the response.
    /// </summary>
    public int? HistoryKeepCount { get; init; }

    /// <summary>
    /// True when this turn was declined because the conversation exhausted its lifetime token budget.
    /// A graceful stop, not an error: <see cref="Success"/> stays true and <see cref="Response"/> carries
    /// the explanatory message. The transport may surface this (e.g. disable further input).
    /// </summary>
    public bool BudgetExhausted { get; init; }
}
