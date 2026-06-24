using Domain.AI.Budget;

namespace Application.AI.Common.Interfaces.AI;

/// <summary>
/// Tracks cumulative token consumption across <em>all turns of a single conversation</em> and reports
/// when a conversation has exhausted its lifetime budget so the caller can stop gracefully.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <strong>singleton</strong> keyed internally by conversation id: a conversation spans
/// many turns (each its own MediatR request scope), so the tracker must outlive any one scope — unlike
/// the per-turn scoped <see cref="ITokenBudgetTracker"/>, which caps a single turn and is re-seeded each
/// request. The two are complementary: <see cref="ITokenBudgetTracker"/> bounds intra-turn cost (and
/// throws on a pre-flight overage); this tracker bounds whole-conversation cost and is consulted
/// <em>between</em> turns to break the loop gracefully — it never throws.
/// </para>
/// <para>
/// Implementations are thread-safe and bound their memory: a long-lived interactive deployment can
/// accumulate many conversations, so entries are capped and evicted rather than retained forever.
/// </para>
/// </remarks>
public interface IConversationBudgetTracker
{
    /// <summary>
    /// Adds a completed turn's token usage to the conversation's running total, creating the
    /// conversation's entry (seeded from configuration) on first use.
    /// </summary>
    /// <param name="conversationId">The conversation the usage belongs to.</param>
    /// <param name="tokensUsed">Input+output tokens consumed by the turn. Non-negative; zero is a no-op.</param>
    void RecordUsage(string conversationId, int tokensUsed);

    /// <summary>
    /// Returns the conversation's current budget status. When no budget is configured, or the
    /// conversation has no recorded usage yet, returns a status whose <see cref="ConversationBudgetStatus.IsExhausted"/>
    /// reflects the configured ceiling (disabled ceilings never report exhausted).
    /// </summary>
    /// <param name="conversationId">The conversation to query.</param>
    ConversationBudgetStatus GetStatus(string conversationId);

    /// <summary>
    /// Drops the conversation's tracked usage, freeing its entry. Call when a conversation ends (e.g. the
    /// batch conversation loop completes) so the singleton does not retain state indefinitely. Safe to
    /// call for an unknown conversation id. Bounded eviction also reclaims abandoned entries, so this is
    /// an optimisation, not a correctness requirement.
    /// </summary>
    /// <param name="conversationId">The conversation to release.</param>
    void Release(string conversationId);
}
