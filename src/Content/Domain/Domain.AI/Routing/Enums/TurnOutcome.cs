namespace Domain.AI.Routing.Enums;

/// <summary>
/// Outcome signal for a completed agent turn.
/// Fed to <see cref="Domain.AI.Routing.Models.ModelTier"/> escalation tracking.
/// </summary>
public enum TurnOutcome
{
    /// <summary>Turn completed successfully, user moved on.</summary>
    Success,

    /// <summary>User corrected the response ("no", "that's wrong").</summary>
    UserCorrection,

    /// <summary>User asked to try again or rephrase.</summary>
    RetryRequested,

    /// <summary>A tool call failed during the turn.</summary>
    ToolFailure,

    /// <summary>Model response timed out.</summary>
    Timeout
}
