namespace Presentation.AgentHub.DTOs;

/// <summary>
/// Wire DTO for the loaded-body deep-link
/// (<c>GET /api/sessions/{id}/turns/{turnIndex}/loaded/{loadedIndex}/body</c>).
/// Carries the full body text captured for a single Foresight
/// <c>LoadedItem</c> — the composed system prompt for a System item, the
/// instructions text for a Skill, the JSON schema for a Tool / MCP tool, or
/// the description for a sub-agent.
/// </summary>
/// <remarks>
/// <see cref="Body"/> is untrusted content (LLM-facing prompt material) —
/// render escaped on the client. <c>null</c> when no body was captured
/// (Messages-category items have their own deep-link; older rows from
/// before body capture also fall through to null).
/// </remarks>
public sealed record LoadedBodyDto(
    string ConversationId,
    int TurnIndex,
    int LoadedIndex,
    string? Body);
