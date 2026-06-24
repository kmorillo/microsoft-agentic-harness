namespace Domain.AI.Budget;

/// <summary>
/// An immutable snapshot of a single conversation's cumulative token budget, spanning every turn of
/// that conversation. Produced by <c>IConversationBudgetTracker</c> and consulted between turns to
/// decide whether the conversation may continue.
/// </summary>
/// <param name="IsEnabled">
/// Whether a conversation-lifetime budget is in force. False when no positive ceiling is configured
/// (<c>AppConfig.AI.AgentFramework.ConversationTokenBudget</c> ≤ 0), in which case <see cref="IsExhausted"/>
/// is always false and conversations run unbounded across turns.
/// </param>
/// <param name="TotalBudget">The configured cumulative token ceiling for the conversation. Zero when disabled.</param>
/// <param name="ConsumedTokens">The total input+output tokens recorded across the conversation so far.</param>
public sealed record ConversationBudgetStatus(bool IsEnabled, int TotalBudget, int ConsumedTokens)
{
    /// <summary>A disabled status: no ceiling in force, nothing consumed.</summary>
    public static ConversationBudgetStatus Disabled { get; } = new(false, 0, 0);

    /// <summary>Tokens remaining before the ceiling, floored at zero. Always zero when disabled.</summary>
    public int RemainingBudget => IsEnabled ? Math.Max(0, TotalBudget - ConsumedTokens) : 0;

    /// <summary>
    /// True when an enabled budget has been fully consumed. The next turn should be refused gracefully.
    /// Always false when <see cref="IsEnabled"/> is false, so disabled conversations never break.
    /// </summary>
    public bool IsExhausted => IsEnabled && ConsumedTokens >= TotalBudget;
}
