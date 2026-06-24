namespace Presentation.AgentHub.Services;

/// <summary>
/// Single source of the user-facing copy shown when a conversation is declined because it exhausted
/// its lifetime token budget. Shared by the SignalR (<see cref="ConversationOrchestrator"/>) and AG-UI
/// (<c>AgUiRunHandler</c>) transports so the two paths cannot drift.
/// </summary>
internal static class ConversationBudgetNotice
{
    /// <summary>The message returned to the user in place of a declined turn.</summary>
    public const string Message =
        "This conversation has reached its token budget. Please start a new conversation to continue.";
}
