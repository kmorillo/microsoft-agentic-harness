namespace Domain.AI.SkillTraining;

/// <summary>
/// An advisory, never-applied proposal to change one bounded harness configuration field — the
/// suggestion-only path of Self-Harness Phase 2 Step 2.
/// </summary>
/// <remarks>
/// <para>
/// Step 1 widened the loop to edit prose <em>surfaces</em> (skill text, recovery guidance) and prove
/// each edit through the accept-gate. Some harness knobs, though, are not prose but plain settings —
/// the canonical example is the transient-failure retry dial (<c>RetryConfig.MaxAttempts</c>). A scalar
/// config change cannot flow through the rollout-scoring gate (there is no trajectory whose score moves
/// because an <c>int</c> changed), and even if it could, mutating live resilience configuration from a
/// self-optimization loop is exactly the kind of self-modification Phase 2 deliberately refuses to
/// automate.
/// </para>
/// <para>
/// So this type is a <em>note to a human</em>, not a hand on the dial. The loop may emit one; a
/// code-owned <c>ConfigSurfaceConstraint</c> + <c>HarnessChangeSuggestionValidator</c> bounds-check it;
/// valid suggestions are written to the tamper-evident governance audit and surfaced on the run result.
/// Nothing here ever mutates running configuration. All values are carried as strings so the Domain
/// stays decoupled from the concrete config POCOs (which live in <c>Domain.Common.Config</c>) — the
/// suggestion is advisory text, not a typed assignment.
/// </para>
/// </remarks>
public sealed record HarnessChangeSuggestion
{
    /// <summary>
    /// The harness surface the suggestion concerns — e.g. <see cref="HarnessSurface.ToolErrorRetryLimit"/>
    /// for the retry dial. The constraint rejects any surface it does not govern.
    /// </summary>
    public required HarnessSurface Surface { get; init; }

    /// <summary>
    /// The configuration field the suggestion targets (e.g. <c>"MaxAttempts"</c>). Must be one of the
    /// constraint's allowed fields; frozen fields (delay, backoff type) are rejected.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>The field's current value, as observed by the loop. Advisory context only.</summary>
    public required string CurrentValue { get; init; }

    /// <summary>
    /// The proposed new value. Bounds-checked by the constraint (e.g. an integer within
    /// <c>[2, 5]</c> for <c>MaxAttempts</c>); out-of-range or non-parsable values are rejected.
    /// </summary>
    public required string ProposedValue { get; init; }

    /// <summary>
    /// Why the loop is proposing this change — the rollout signal that motivated it (e.g. "12 of 16
    /// rollouts failed on transient tool errors"). Surfaced verbatim to the human reviewer.
    /// </summary>
    public string Rationale { get; init; } = string.Empty;
}
