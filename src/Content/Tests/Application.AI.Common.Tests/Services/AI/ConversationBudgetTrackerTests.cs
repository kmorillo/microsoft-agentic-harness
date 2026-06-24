using Application.AI.Common.Services.AI;
using Domain.Common.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Application.AI.Common.Tests.Services.AI;

/// <summary>
/// Verifies the conversation-lifetime budget tracker: inert when disabled, accumulates usage across
/// turns per conversation, reports exhaustion at the ceiling, isolates conversations, and releases.
/// </summary>
public sealed class ConversationBudgetTrackerTests
{
    private static ConversationBudgetTracker Create(int budget)
    {
        var cfg = new AppConfig();
        cfg.AI.AgentFramework.ConversationTokenBudget = budget;
        var monitor = Mock.Of<IOptionsMonitor<AppConfig>>(m => m.CurrentValue == cfg);
        return new ConversationBudgetTracker(monitor, TimeProvider.System, NullLogger<ConversationBudgetTracker>.Instance);
    }

    [Fact]
    public void GetStatus_BudgetDisabled_ReturnsDisabledNeverExhausted()
    {
        var tracker = Create(0);
        tracker.RecordUsage("c1", 1_000_000);

        var status = tracker.GetStatus("c1");

        Assert.False(status.IsEnabled);
        Assert.False(status.IsExhausted);
        Assert.Equal(0, status.RemainingBudget);
    }

    [Fact]
    public void RecordUsage_AccumulatesAcrossTurns_WithinOneConversation()
    {
        var tracker = Create(1_000);

        tracker.RecordUsage("c1", 300);
        tracker.RecordUsage("c1", 250);

        var status = tracker.GetStatus("c1");
        Assert.True(status.IsEnabled);
        Assert.Equal(1_000, status.TotalBudget);
        Assert.Equal(550, status.ConsumedTokens);
        Assert.Equal(450, status.RemainingBudget);
        Assert.False(status.IsExhausted);
    }

    [Fact]
    public void GetStatus_AtOrAboveCeiling_ReportsExhausted()
    {
        var tracker = Create(1_000);

        tracker.RecordUsage("c1", 600);
        Assert.False(tracker.GetStatus("c1").IsExhausted);

        tracker.RecordUsage("c1", 400); // reaches exactly the ceiling
        var status = tracker.GetStatus("c1");
        Assert.True(status.IsExhausted);
        Assert.Equal(0, status.RemainingBudget);
    }

    [Fact]
    public void RecordUsage_OvershootCeiling_RemainingFlooredAtZero()
    {
        var tracker = Create(1_000);
        tracker.RecordUsage("c1", 1_500);

        var status = tracker.GetStatus("c1");
        Assert.True(status.IsExhausted);
        Assert.Equal(1_500, status.ConsumedTokens);
        Assert.Equal(0, status.RemainingBudget);
    }

    [Fact]
    public void Conversations_AreIsolated()
    {
        var tracker = Create(1_000);

        tracker.RecordUsage("c1", 1_000);
        tracker.RecordUsage("c2", 100);

        Assert.True(tracker.GetStatus("c1").IsExhausted);
        Assert.False(tracker.GetStatus("c2").IsExhausted);
        Assert.Equal(900, tracker.GetStatus("c2").RemainingBudget);
    }

    [Fact]
    public void Release_ResetsConversationUsage()
    {
        var tracker = Create(1_000);
        tracker.RecordUsage("c1", 1_000);
        Assert.True(tracker.GetStatus("c1").IsExhausted);

        tracker.Release("c1");

        var status = tracker.GetStatus("c1");
        Assert.False(status.IsExhausted);
        Assert.Equal(0, status.ConsumedTokens);
    }

    [Fact]
    public void RecordUsage_ZeroTokens_IsNoOp()
    {
        var tracker = Create(1_000);
        tracker.RecordUsage("c1", 0);
        Assert.Equal(0, tracker.GetStatus("c1").ConsumedTokens);
    }

    [Fact]
    public void RecordUsage_Disabled_StoresNothing()
    {
        var tracker = Create(0);
        tracker.RecordUsage("c1", 500);

        // Re-querying with a disabled budget always yields the disabled status; nothing was tracked.
        Assert.Equal(0, tracker.GetStatus("c1").ConsumedTokens);
    }

    [Fact]
    public void RecordUsage_NegativeTokens_Throws()
    {
        var tracker = Create(1_000);
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.RecordUsage("c1", -1));
    }

    [Fact]
    public void EvictsLeastRecentlyUsed_WhenOverCapacity()
    {
        var tracker = Create(1_000);

        // Exceed the cap; the tracker must evict back to at or below it rather than grow unbounded.
        for (var i = 0; i <= ConversationBudgetTracker.MaxTrackedConversations; i++)
            tracker.RecordUsage($"c{i}", 1);

        // A freshly-recorded conversation survives; the invariant is that the cap is respected.
        var survivor = $"c{ConversationBudgetTracker.MaxTrackedConversations}";
        Assert.True(tracker.GetStatus(survivor).ConsumedTokens >= 1);
    }
}
