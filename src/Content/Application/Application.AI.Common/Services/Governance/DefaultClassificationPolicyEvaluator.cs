using Application.AI.Common.Interfaces.Governance;
using Domain.AI.Governance;
using Domain.Common.Config.AI.Governance;

namespace Application.AI.Common.Services.Governance;

/// <summary>
/// Default <see cref="IClassificationPolicyEvaluator"/>: a pure projection of a resolved
/// <see cref="AssetLabelResult"/> onto a <see cref="ClassificationPolicyDecision"/> using the
/// label→action map in <see cref="DataClassificationConfig"/>.
/// </summary>
/// <remarks>
/// Decision precedence:
/// <list type="number">
/// <item>No sensitivity label resolved → <see cref="DataClassificationConfig.UnknownAssetAction"/>.</item>
/// <item>Label present and named in <see cref="DataClassificationConfig.LabelActions"/> → that action.</item>
/// <item>Label present but unmapped → <see cref="DataClassificationConfig.DefaultAction"/>.</item>
/// </list>
/// Classifications are carried for audit but do not drive the action in this evaluator — the decision is
/// sensitivity-label driven.
/// </remarks>
public sealed class DefaultClassificationPolicyEvaluator : IClassificationPolicyEvaluator
{
    /// <inheritdoc />
    public ClassificationPolicyDecision Evaluate(AssetLabelResult result, DataClassificationConfig config)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(config);

        if (result.Label is null)
        {
            var reason = result.Source == LabelSource.None
                ? "No classification could be resolved for the asset; applying the unknown-asset policy."
                : "Asset was classified but carries no sensitivity label; applying the unknown-asset policy.";
            return Decide(config.UnknownAssetAction, reason, matchedLabel: null, matchedRule: nameof(config.UnknownAssetAction));
        }

        var labelName = result.Label.Name;

        // Explicit case-insensitive match rather than relying on the dictionary's comparer: IConfiguration
        // binding rebuilds LabelActions with the default ordinal comparer, dropping the POCO's
        // case-insensitive default, so a TryGetValue here would miss "confidential" vs "Confidential".
        foreach (var (ruleLabel, action) in config.LabelActions)
        {
            if (string.Equals(ruleLabel, labelName, StringComparison.OrdinalIgnoreCase))
            {
                return Decide(action,
                    $"Sensitivity label '{labelName}' maps to {action} by policy.",
                    matchedLabel: labelName,
                    matchedRule: $"{nameof(config.LabelActions)}[{ruleLabel}]");
            }
        }

        return Decide(config.DefaultAction,
            $"Sensitivity label '{labelName}' has no explicit rule; applying the default action.",
            matchedLabel: labelName,
            matchedRule: nameof(config.DefaultAction));
    }

    private static ClassificationPolicyDecision Decide(
        ClassificationAction action, string reason, string? matchedLabel, string matchedRule) =>
        action switch
        {
            ClassificationAction.Block => ClassificationPolicyDecision.Block(reason, matchedLabel, matchedRule),
            ClassificationAction.Redact => ClassificationPolicyDecision.Redact(reason, matchedLabel, matchedRule),
            _ => ClassificationPolicyDecision.Allow(reason, matchedLabel, matchedRule)
        };
}
