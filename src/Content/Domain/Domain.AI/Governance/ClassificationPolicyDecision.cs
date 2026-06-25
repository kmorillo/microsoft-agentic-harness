using Domain.Common.Config.AI.Governance;

namespace Domain.AI.Governance;

/// <summary>
/// The outcome of evaluating a data-classification policy against an
/// <see cref="AssetLabelResult"/>: the action to take and why. Immutable value object returned by
/// <c>IClassificationPolicyEvaluator</c>.
/// </summary>
/// <param name="Action">The enforcement action the policy assigned.</param>
/// <param name="Reason">A human-readable explanation, suitable for audit logs and model-facing denials.</param>
/// <param name="MatchedLabel">
/// The sensitivity label that drove the decision, or null when the decision came from the unknown-asset
/// rule (no label resolved).
/// </param>
/// <param name="MatchedRule">
/// The policy rule that produced the action — for example <c>LabelActions[Confidential]</c>,
/// <c>DefaultAction</c>, or <c>UnknownAssetAction</c> — so audit can trace which rule fired.
/// </param>
public sealed record ClassificationPolicyDecision(
    ClassificationAction Action,
    string Reason,
    string? MatchedLabel = null,
    string? MatchedRule = null)
{
    /// <summary>Creates an allow decision.</summary>
    public static ClassificationPolicyDecision Allow(string reason, string? matchedLabel = null, string? matchedRule = null) =>
        new(ClassificationAction.Allow, reason, matchedLabel, matchedRule);

    /// <summary>Creates a redact decision.</summary>
    public static ClassificationPolicyDecision Redact(string reason, string? matchedLabel = null, string? matchedRule = null) =>
        new(ClassificationAction.Redact, reason, matchedLabel, matchedRule);

    /// <summary>Creates a block decision.</summary>
    public static ClassificationPolicyDecision Block(string reason, string? matchedLabel = null, string? matchedRule = null) =>
        new(ClassificationAction.Block, reason, matchedLabel, matchedRule);
}
