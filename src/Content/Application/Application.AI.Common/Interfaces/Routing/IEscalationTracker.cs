// src/Content/Application/Application.AI.Common/Interfaces/Routing/IEscalationTracker.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// Tracks per-conversation quality signals and adjusts model tier accordingly.
/// In-memory state with the same lifetime as the agent conversation cache.
/// </summary>
public interface IEscalationTracker
{
    /// <summary>
    /// Returns the effective tier for a conversation, factoring in recent quality signals.
    /// May return a higher tier than <paramref name="baseComplexity"/> warrants if escalation is active.
    /// </summary>
    ModelTier GetEffectiveTier(
        string conversationId,
        TaskComplexity baseComplexity,
        IReadOnlyList<ModelTier> availableTiers);

    /// <summary>Records a turn outcome for escalation tracking.</summary>
    void RecordOutcome(string conversationId, TurnOutcome outcome);

    /// <summary>Resets escalation state for a conversation (e.g., on conversation end).</summary>
    void Reset(string conversationId);
}
