namespace Domain.AI.Context;

/// <summary>
/// Sidecar to <see cref="LoadedItem"/>: the full body text of a single
/// per-turn loaded artifact (composed system prompt, skill instructions, tool
/// JSON schema, MCP descriptor, sub-agent description). Persisted in a
/// separate row keyed by <c>(conversationId, turnIndex, loadedIndex)</c> so
/// the snapshot row + SignalR wire stay lean — bodies are fetched lazily by
/// the dashboard on drawer-open.
/// </summary>
/// <param name="LoadedIndex">Position in the snapshot's <c>Loaded[]</c> array this body belongs to.</param>
/// <param name="Body">Captured body text. Never <c>null</c>; absent bodies are simply not persisted.</param>
public sealed record LoadedItemBody(int LoadedIndex, string Body);
