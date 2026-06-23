using System.Globalization;
using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// The config-surface fence: bounds-checks every <see cref="HarnessChangeSuggestion"/> against the
/// code-owned <see cref="ConfigSurfaceConstraint"/> before the loop audits or surfaces it.
/// </summary>
/// <remarks>
/// <para>
/// Like <c>HarnessPatchValidator</c>, this is intentionally a concrete sealed class with no interface
/// seam: a validator a consumer could swap for a permissive no-op would not be a fence. The only
/// configurable part of the policy is the <see cref="ConfigSurfaceConstraint"/> it reads — and that
/// constraint's allowlist and bounds are compile-time constants.
/// </para>
/// <para>
/// <see cref="Validate"/> is pure and side-effect free. Auditing a rejection or an acceptance, and
/// surfacing survivors on the run result, are the caller's responsibility (the training loop), which
/// keeps this component deterministically testable.
/// </para>
/// </remarks>
public sealed class HarnessChangeSuggestionValidator
{
    private readonly ConfigSurfaceConstraint _constraint;

    /// <summary>Initializes a new instance of the <see cref="HarnessChangeSuggestionValidator"/> class.</summary>
    /// <param name="constraint">The code-owned config-surface constraint to enforce.</param>
    /// <exception cref="ArgumentNullException"><paramref name="constraint"/> is <see langword="null"/>.</exception>
    public HarnessChangeSuggestionValidator(ConfigSurfaceConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        _constraint = constraint;
    }

    /// <summary>
    /// Validates that <paramref name="suggestion"/> targets the governed surface, names an allowed field,
    /// and proposes an in-bounds value.
    /// </summary>
    /// <param name="suggestion">The suggestion to check.</param>
    /// <returns>
    /// An allowed result (<see cref="HarnessChangeSuggestionValidation.AllowedWith(string)"/>) carrying
    /// the scrubbed canonical value when all checks pass; otherwise a rejection carrying the first failing
    /// <see cref="HarnessChangeRejectionReason"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="suggestion"/> is <see langword="null"/>.</exception>
    public HarnessChangeSuggestionValidation Validate(HarnessChangeSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        if (!_constraint.GovernsSurface(suggestion.Surface))
        {
            return HarnessChangeSuggestionValidation.Rejected(HarnessChangeRejectionReason.UngovernedSurface);
        }

        if (!_constraint.IsFieldAllowed(suggestion.Field))
        {
            return HarnessChangeSuggestionValidation.Rejected(HarnessChangeRejectionReason.FieldNotAllowed);
        }

        if (!int.TryParse(suggestion.ProposedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return HarnessChangeSuggestionValidation.Rejected(HarnessChangeRejectionReason.ValueUnparsable);
        }

        return _constraint.IsWithinBounds(suggestion.Field, value)
            ? HarnessChangeSuggestionValidation.AllowedWith(value.ToString(CultureInfo.InvariantCulture))
            : HarnessChangeSuggestionValidation.Rejected(HarnessChangeRejectionReason.ValueOutOfBounds);
    }
}
