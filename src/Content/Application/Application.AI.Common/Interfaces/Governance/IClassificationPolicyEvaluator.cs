using Domain.AI.Governance;
using Domain.Common.Config.AI.Governance;

namespace Application.AI.Common.Interfaces.Governance;

/// <summary>
/// Maps a resolved <see cref="AssetLabelResult"/> to a <see cref="ClassificationPolicyDecision"/> using
/// the operator's <see cref="DataClassificationConfig"/>. Pure and deterministic — no I/O, no model.
/// </summary>
/// <remarks>
/// The evaluator computes the <em>decision</em> (allow / redact / block) from the label and the policy
/// map. It deliberately does not consider <see cref="DataClassificationConfig.Mode"/>: whether a block
/// decision is actually enforced or merely observed is the enforcement point's responsibility, keeping
/// this component a pure projection that is trivial to test.
/// </remarks>
public interface IClassificationPolicyEvaluator
{
    /// <summary>
    /// Evaluates the policy against a resolved classification result.
    /// </summary>
    /// <param name="result">The label/classifications resolved for the asset.</param>
    /// <param name="config">The classification policy to apply.</param>
    /// <returns>The action to take, with the matched rule and a human-readable reason.</returns>
    ClassificationPolicyDecision Evaluate(AssetLabelResult result, DataClassificationConfig config);
}
