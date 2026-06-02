using Domain.AI.Observability.Models;

namespace Presentation.AgentHub.DTOs;

/// <summary>
/// Wire DTO for the per-invocation deep-link
/// (<c>GET /api/sessions/{id}/tools/{invocationId}</c>). Mirrors
/// <see cref="ToolExecutionRecord"/> exactly so a future Domain rename
/// surfaces as a compile error here.
/// </summary>
/// <remarks>
/// The dashboard renders <see cref="Args"/> and <see cref="Stdout"/> as
/// untrusted text — both are LLM-supplied content from the agent turn.
/// </remarks>
public sealed record ToolInvocationDetailDto(
    Guid Id,
    Guid SessionId,
    Guid? MessageId,
    string ToolName,
    string? ToolSource,
    int? DurationMs,
    string Status,
    string? ErrorType,
    int? ResultSize,
    string? CallId,
    string? Args,
    string? Stdout,
    DateTimeOffset CreatedAt)
{
    /// <summary>Projects a domain record onto the wire DTO.</summary>
    public static ToolInvocationDetailDto From(ToolExecutionRecord r) => new(
        r.Id,
        r.SessionId,
        r.MessageId,
        r.ToolName,
        r.ToolSource,
        r.DurationMs,
        r.Status,
        r.ErrorType,
        r.ResultSize,
        r.CallId,
        r.Args,
        r.Stdout,
        r.CreatedAt);
}
