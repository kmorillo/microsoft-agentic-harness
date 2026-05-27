namespace Presentation.AgentHub.DTOs;

/// <summary>
/// Tracks per-connection conversation state for lifecycle metrics on disconnect.
/// Keyed by transport connection identifier (e.g. SignalR ConnectionId).
/// </summary>
public sealed record ActiveConversationInfo(
    string ConversationId, string AgentName, string UserId, DateTimeOffset StartedAt, int TurnCount,
    Guid ObservabilitySessionId, int TotalInputTokens = 0, int TotalOutputTokens = 0,
    int TotalCacheRead = 0, int TotalCacheWrite = 0, decimal TotalCostUsd = 0m, int ToolCallCount = 0)
{
    public DateTimeOffset LastActivityAt { get; init; } = DateTimeOffset.UtcNow;
}
