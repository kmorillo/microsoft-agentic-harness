using System.Collections.Concurrent;
using Application.AI.Common.Interfaces.Context;

namespace Application.AI.Common.Services.Context;

/// <summary>
/// In-memory <see cref="IConversationRegistrationTracker"/>. Registered as a singleton
/// so a single instance spans the process; per-conversation state is keyed by id and
/// cleared on <see cref="Evict"/> or when the conversation is dropped from the agent
/// cache. Capacity is bounded by the agent-cache TTL (30 min) in practice — callers
/// that hold conversations longer should evict explicitly.
/// </summary>
public sealed class ConversationRegistrationTracker : IConversationRegistrationTracker
{
    private readonly ConcurrentDictionary<string, ConversationState> _state = new();

    public RegistrationDelta DiffAndUpdate(string conversationId, RegistrationSnapshot current)
    {
        // Read-then-replace under the per-conversation lock so two concurrent turns
        // for the same conversation can't both report "first sighting" for the same
        // tool. Inter-conversation contention stays zero thanks to the dictionary.
        var slot = _state.GetOrAdd(conversationId, _ => new ConversationState());
        lock (slot.Sync)
        {
            var prev = slot.Snapshot;
            var delta = ComputeDelta(prev, current);
            slot.Snapshot = current;
            return delta;
        }
    }

    public void Evict(string conversationId) => _state.TryRemove(conversationId, out _);

    private static RegistrationDelta ComputeDelta(RegistrationSnapshot? prev, RegistrationSnapshot current)
    {
        if (prev is null)
        {
            // First turn — everything is new. The "system prompt is new" flag fires
            // whenever the text is non-empty so we don't emit an empty system row.
            return new RegistrationDelta(
                SystemPromptIsNew: !string.IsNullOrEmpty(current.SystemPromptText),
                NewSkills: current.Skills,
                NewNativeTools: current.NativeTools,
                NewMcpTools: current.McpTools,
                NewSubAgents: current.SubAgents);
        }

        var newSkills = DiffByKey(prev.Skills, current.Skills, s => s.Id);
        var newNative = DiffByKey(prev.NativeTools, current.NativeTools, t => t.Name);
        var newMcp = DiffByKey(prev.McpTools, current.McpTools, t => t.Name);
        var newAgents = DiffByKey(prev.SubAgents, current.SubAgents, a => a.Id);
        var systemChanged = !string.Equals(prev.SystemPromptText, current.SystemPromptText, StringComparison.Ordinal)
                             && !string.IsNullOrEmpty(current.SystemPromptText);

        return new RegistrationDelta(systemChanged, newSkills, newNative, newMcp, newAgents);
    }

    private static IReadOnlyList<T> DiffByKey<T>(
        IReadOnlyList<T> prev,
        IReadOnlyList<T> current,
        Func<T, string> keySelector)
    {
        if (current.Count == 0) return [];
        var seen = new HashSet<string>(prev.Select(keySelector), StringComparer.OrdinalIgnoreCase);
        var added = new List<T>();
        foreach (var item in current)
            if (seen.Add(keySelector(item)))
                added.Add(item);
        return added;
    }

    private sealed class ConversationState
    {
        public RegistrationSnapshot? Snapshot;
        public readonly object Sync = new();
    }
}
