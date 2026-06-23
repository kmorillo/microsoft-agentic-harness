namespace Domain.AI.SkillTraining;

/// <summary>
/// The result of checking a <see cref="HarnessChangeSuggestion"/> against the code-owned
/// <c>ConfigSurfaceConstraint</c> before it is audited or surfaced on a run result.
/// </summary>
/// <remarks>
/// A suggestion is allowed only when it targets the constrained surface, names an allowed field, and
/// proposes a value within that field's bounds. Anything else is rejected with a stable, scrubbed
/// <see cref="RejectionReason"/> so the audit trail distinguishes <em>why</em> a suggestion was refused
/// without leaking proposer internals.
/// </remarks>
public sealed record HarnessChangeSuggestionValidation
{
    /// <summary>True iff the suggestion targets an allowed field with an in-bounds value.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// The reason category when <see cref="IsAllowed"/> is false; <see cref="HarnessChangeRejectionReason.None"/>
    /// when allowed.
    /// </summary>
    public HarnessChangeRejectionReason RejectionReason { get; init; } = HarnessChangeRejectionReason.None;

    /// <summary>
    /// The canonical, parsed form of the validated value when <see cref="IsAllowed"/> is
    /// <see langword="true"/>; <see langword="null"/> when rejected. The loop audits <em>this</em> — not
    /// the raw proposer-supplied <c>ProposedValue</c> — so the tamper-evident trail records a scrubbed
    /// canonical value (e.g. <c>"3"</c>), never arbitrary proposer text such as <c>" +0003 "</c>.
    /// </summary>
    public string? NormalizedValue { get; init; }

    /// <summary>A shared allowed result carrying no normalized value.</summary>
    public static HarnessChangeSuggestionValidation Allowed { get; } = new() { IsAllowed = true };

    /// <summary>Builds an allowed result carrying the scrubbed canonical <paramref name="normalizedValue"/>.</summary>
    /// <param name="normalizedValue">The validated value in canonical form, safe to audit.</param>
    public static HarnessChangeSuggestionValidation AllowedWith(string normalizedValue) =>
        new() { IsAllowed = true, NormalizedValue = normalizedValue };

    /// <summary>Builds a rejection result with the given <paramref name="reason"/>.</summary>
    /// <param name="reason">Why the suggestion was rejected.</param>
    public static HarnessChangeSuggestionValidation Rejected(HarnessChangeRejectionReason reason) =>
        new() { IsAllowed = false, RejectionReason = reason };
}

/// <summary>
/// Why a <see cref="HarnessChangeSuggestion"/> was rejected by the constraint. A closed set of stable
/// categories — not free text — so the governance audit records the failure mode without echoing any
/// proposer-supplied content.
/// </summary>
public enum HarnessChangeRejectionReason
{
    /// <summary>The suggestion was allowed (no rejection).</summary>
    None,

    /// <summary>The suggestion targets a surface the constraint does not govern.</summary>
    UngovernedSurface,

    /// <summary>The named field is not in the constraint's allowed-field set (it is frozen).</summary>
    FieldNotAllowed,

    /// <summary>The proposed value could not be parsed as the field's expected type.</summary>
    ValueUnparsable,

    /// <summary>The proposed value parsed but fell outside the field's allowed bounds.</summary>
    ValueOutOfBounds
}
