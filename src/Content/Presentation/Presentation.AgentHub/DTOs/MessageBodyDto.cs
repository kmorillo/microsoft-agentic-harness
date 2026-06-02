using Domain.AI.Observability.Models;

namespace Presentation.AgentHub.DTOs;

/// <summary>
/// Wire DTO for the file-body deep-link
/// (<c>GET /api/sessions/{id}/messages/{messageId}</c>). Carries the
/// original content captured by the agent turn handler before the 500-char
/// preview truncation.
/// </summary>
/// <remarks>
/// <see cref="ContentFull"/> is untrusted content — render escaped on the
/// client. <c>null</c> when the message predates the content_full column or
/// the row was inserted with the preview-only path.
/// </remarks>
public sealed record MessageBodyDto(
    Guid Id,
    Guid SessionId,
    int TurnIndex,
    string Role,
    string? Source,
    string? ContentPreview,
    string? ContentFull,
    string? Model,
    DateTimeOffset CreatedAt)
{
    /// <summary>Projects a domain record onto the wire DTO.</summary>
    public static MessageBodyDto From(SessionMessageRecord r) => new(
        r.Id,
        r.SessionId,
        r.TurnIndex,
        r.Role,
        r.Source,
        r.ContentPreview,
        r.ContentFull,
        r.Model,
        r.CreatedAt);
}
