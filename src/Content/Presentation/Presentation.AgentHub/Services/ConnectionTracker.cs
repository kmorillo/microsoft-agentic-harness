using System.Collections.Concurrent;
using Presentation.AgentHub.DTOs;
using Presentation.AgentHub.Interfaces;

namespace Presentation.AgentHub.Services;

/// <summary>
/// Thread-safe singleton that tracks per-connection conversation state.
/// Replaces the static <c>ConcurrentDictionary</c> that previously lived on the hub class.
/// </summary>
public sealed class ConnectionTracker : IConnectionTracker
{
    private readonly ConcurrentDictionary<string, ActiveConversationInfo> _connections = new();

    public ActiveConversationInfo? Get(string connectionId) =>
        _connections.TryGetValue(connectionId, out var info) ? info : null;

    public void Track(string connectionId, ActiveConversationInfo info) =>
        _connections[connectionId] = info;

    public ActiveConversationInfo? Untrack(string connectionId) =>
        _connections.TryRemove(connectionId, out var info) ? info : null;

    public IEnumerable<KeyValuePair<string, ActiveConversationInfo>> GetAll() => _connections;
}
