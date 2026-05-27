using Presentation.AgentHub.DTOs;

namespace Presentation.AgentHub.Interfaces;

/// <summary>
/// Singleton registry of per-connection conversation state. Replaces the static
/// <c>ConcurrentDictionary</c> that previously lived on the hub class.
/// Keyed by transport connection identifier (e.g. SignalR ConnectionId).
/// </summary>
public interface IConnectionTracker
{
    /// <summary>Returns the tracked info for <paramref name="connectionId"/>, or null if untracked.</summary>
    ActiveConversationInfo? Get(string connectionId);

    /// <summary>Registers or replaces the tracked info for <paramref name="connectionId"/>.</summary>
    void Track(string connectionId, ActiveConversationInfo info);

    /// <summary>Removes and returns the tracked info for <paramref name="connectionId"/>, or null if absent.</summary>
    ActiveConversationInfo? Untrack(string connectionId);

    /// <summary>Returns all tracked connections. Used by idle-session cleanup.</summary>
    IEnumerable<KeyValuePair<string, ActiveConversationInfo>> GetAll();
}
