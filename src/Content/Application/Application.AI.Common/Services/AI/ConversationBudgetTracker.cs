using System.Collections.Concurrent;
using Application.AI.Common.Interfaces.AI;
using Domain.AI.Budget;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.AI.Common.Services.AI;

/// <summary>
/// Thread-safe singleton that accumulates per-conversation token usage across turns and reports
/// whether a conversation has exhausted its lifetime budget. Seeds each conversation's ceiling from
/// <c>AppConfig.AI.AgentFramework.ConversationTokenBudget</c>, read live so a config reload takes effect.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Opt-in.</strong> When the configured ceiling is ≤ 0 the tracker is inert: <see cref="GetStatus"/>
/// returns <see cref="ConversationBudgetStatus.Disabled"/> and <see cref="RecordUsage"/> stores nothing, so
/// the default deployment does no per-call dictionary work and conversations run unbounded across turns.
/// </para>
/// <para>
/// <strong>Bounded memory.</strong> A long-lived interactive host can see many conversations and there is
/// no universal "conversation ended" signal, so entries are capped at <see cref="MaxTrackedConversations"/>
/// and the least-recently-touched entries are evicted when the cap is exceeded. Eviction can let an
/// evicted-then-resumed conversation's running total reset (under-enforcing its budget) — a bounded,
/// documented trade-off preferred over unbounded growth. Callers with a clear lifecycle should call
/// <see cref="Release"/> on completion.
/// </para>
/// </remarks>
public sealed class ConversationBudgetTracker : IConversationBudgetTracker
{
    /// <summary>Maximum number of conversations tracked before least-recently-used eviction kicks in.</summary>
    internal const int MaxTrackedConversations = 50_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _evictionLock = new();
    private readonly IOptionsMonitor<AppConfig> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConversationBudgetTracker> _logger;

    public ConversationBudgetTracker(
        IOptionsMonitor<AppConfig> options,
        TimeProvider timeProvider,
        ILogger<ConversationBudgetTracker> logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private int ConfiguredBudget => _options.CurrentValue.AI.AgentFramework.ConversationTokenBudget;

    /// <inheritdoc />
    public void RecordUsage(string conversationId, int tokensUsed)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId);
        ArgumentOutOfRangeException.ThrowIfNegative(tokensUsed);

        // Disabled or no-op: store nothing so the default (unbounded) deployment never allocates.
        if (tokensUsed == 0 || ConfiguredBudget <= 0)
            return;

        var now = _timeProvider.GetUtcNow().UtcTicks;
        // Stamp the access time at creation so a brand-new entry is never rank-0 (oldest) for a
        // concurrent eviction running before this thread updates the timestamp.
        var entry = _entries.GetOrAdd(conversationId, _ => new Entry { LastAccessTicks = now });
        entry.Add(tokensUsed);
        entry.LastAccessTicks = now;

        EvictIfOverCapacity();
    }

    /// <inheritdoc />
    public ConversationBudgetStatus GetStatus(string conversationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId);

        var budget = ConfiguredBudget;
        if (budget <= 0)
            return ConversationBudgetStatus.Disabled;

        if (!_entries.TryGetValue(conversationId, out var entry))
            return new ConversationBudgetStatus(true, budget, 0);

        entry.LastAccessTicks = _timeProvider.GetUtcNow().UtcTicks;
        return new ConversationBudgetStatus(true, budget, entry.Consumed);
    }

    /// <inheritdoc />
    public void Release(string conversationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId);
        _entries.TryRemove(conversationId, out _);
    }

    /// <summary>
    /// When the entry count exceeds the cap, evicts the least-recently-touched entries back down to ~90%
    /// of the cap in a single guarded pass, so concurrent writers don't each scan. Eviction is rare
    /// (only at cap) and abandoned conversations are exactly the ones it reclaims.
    /// </summary>
    private void EvictIfOverCapacity()
    {
        if (_entries.Count <= MaxTrackedConversations)
            return;

        lock (_evictionLock)
        {
            if (_entries.Count <= MaxTrackedConversations)
                return;

            var target = (int)(MaxTrackedConversations * 0.9);
            var toRemove = _entries.Count - target;

            // Snapshot keys ordered by oldest access and drop the oldest `toRemove`.
            var oldest = _entries
                .OrderBy(kvp => kvp.Value.LastAccessTicks)
                .Take(toRemove)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldest)
                _entries.TryRemove(key, out _);

            _logger.LogWarning(
                "Conversation budget tracker evicted {Count} least-recently-used entries (cap {Cap})",
                oldest.Count, MaxTrackedConversations);
        }
    }

    /// <summary>Per-conversation running total. <see cref="LastAccessTicks"/> drives LRU eviction.</summary>
    private sealed class Entry
    {
        private long _consumed;
        private long _lastAccessTicks;

        /// <summary>
        /// Last access time in UTC ticks. Read/written via <see cref="Volatile"/> so the unlocked
        /// updates in <c>RecordUsage</c>/<c>GetStatus</c> and the read during eviction's ordering are
        /// not torn on a 32-bit runtime (a 64-bit long write is not otherwise guaranteed atomic).
        /// </summary>
        public long LastAccessTicks
        {
            get => Volatile.Read(ref _lastAccessTicks);
            set => Volatile.Write(ref _lastAccessTicks, value);
        }

        /// <summary>Running total, clamped to <see cref="int.MaxValue"/> for the status projection.</summary>
        public int Consumed => (int)Math.Min(int.MaxValue, Interlocked.Read(ref _consumed));

        public void Add(int tokens) => Interlocked.Add(ref _consumed, tokens);
    }
}
