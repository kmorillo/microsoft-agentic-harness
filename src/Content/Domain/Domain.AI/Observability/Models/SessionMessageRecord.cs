namespace Domain.AI.Observability.Models;

/// <summary>
/// Represents a single turn/message within a conversation session.
/// </summary>
public sealed record SessionMessageRecord
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public int TurnIndex { get; init; }
    public required string Role { get; init; }
    public string? Source { get; init; }
    public string? ContentPreview { get; init; }

    /// <summary>
    /// Full message body, captured verbatim before the 500-char preview
    /// truncation. Used by the file-body deep-link endpoint
    /// (<c>GET /api/sessions/{id}/messages/{messageId}</c>) so the dashboard
    /// can render the complete original content. Untrusted — render escaped.
    /// </summary>
    public string? ContentFull { get; init; }
    public string? Model { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheRead { get; init; }
    public int CacheWrite { get; init; }
    public decimal CostUsd { get; init; }
    public decimal CacheHitPct { get; init; }
    public string[]? ToolNames { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
