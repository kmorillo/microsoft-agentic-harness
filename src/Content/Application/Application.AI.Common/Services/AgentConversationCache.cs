using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.Context;
using Domain.AI.Agents;
using Domain.AI.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Caching.Memory;

namespace Application.AI.Common.Services;

/// <summary>
/// <see cref="IMemoryCache"/>-backed implementation of <see cref="IAgentConversationCache"/>.
/// Agents are evicted explicitly on conversation end or automatically after 30 minutes of inactivity.
/// </summary>
internal sealed class AgentConversationCache : IAgentConversationCache
{
    private readonly IMemoryCache _cache;
    private readonly IAgentFactory _agentFactory;
    private readonly IConversationRegistrationTracker _registrationTracker;
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(30);

    private static string ContextCacheKey(string conversationId) => $"{conversationId}::context";

    public AgentConversationCache(
        IMemoryCache cache,
        IAgentFactory agentFactory,
        IConversationRegistrationTracker registrationTracker)
    {
        _cache = cache;
        _agentFactory = agentFactory;
        _registrationTracker = registrationTracker;
    }

    public async Task<AIAgent> GetOrCreateAsync(
        string conversationId,
        IReadOnlyList<string> skillIds,
        SkillAgentOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(conversationId, out AIAgent? cached) && cached is not null)
            return cached;

        var built = await _agentFactory.CreateAgentWithContextFromSkillsAsync(
            skillIds, options, cancellationToken);

        var entryOptions = new MemoryCacheEntryOptions { SlidingExpiration = SlidingExpiration };
        _cache.Set(conversationId, built.Agent, entryOptions);
        _cache.Set(ContextCacheKey(conversationId), built.Context, entryOptions);

        return built.Agent;
    }

    public AgentExecutionContext? TryGetContext(string conversationId)
        => _cache.TryGetValue(ContextCacheKey(conversationId), out AgentExecutionContext? ctx) ? ctx : null;

    public void Evict(string conversationId)
    {
        _cache.Remove(conversationId);
        _cache.Remove(ContextCacheKey(conversationId));
        _registrationTracker.Evict(conversationId);
    }
}
