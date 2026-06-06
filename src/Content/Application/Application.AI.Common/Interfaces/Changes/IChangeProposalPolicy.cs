using Domain.AI.Changes;

namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// One pluggable policy evaluated by the <c>PolicyGate</c>. Adapters for Checkov,
/// OPA, Kyverno, and consumer-defined policies all implement this interface.
/// </summary>
/// <remarks>
/// <para>
/// Each policy is registered keyed (<c>AddKeyedTransient&lt;IChangeProposalPolicy&gt;("checkov", ...)</c>)
/// and the <c>PolicyGate</c> aggregates findings from every registered policy. The
/// gate's pass/fail decision is computed from the highest severity in the union
/// of findings versus the configured threshold from <c>AppConfig.AI.Changes.Policy</c>.
/// </para>
/// <para>
/// Implementations should return an empty list rather than a null when no findings
/// apply — null breaks the gate's aggregation semantics. Implementations should
/// also not throw for "policy doesn't apply to this target type"; instead return
/// empty findings and let the gate move on.
/// </para>
/// </remarks>
public interface IChangeProposalPolicy
{
    /// <summary>
    /// The string key this policy registers under in keyed DI. Used on every
    /// <see cref="PolicyFinding.PolicyKey"/> so dashboards and audit can group by
    /// originating policy. Convention: lowercase, matches the underlying backend
    /// (<c>checkov</c>, <c>opa</c>, <c>kyverno</c>) or a consumer namespace
    /// (<c>acme.tagging</c>).
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Evaluate the proposal and return any findings.
    /// </summary>
    /// <param name="proposal">The proposal under evaluation. Must not be mutated.</param>
    /// <param name="context">Per-evaluation orchestrator context.</param>
    /// <param name="cancellationToken">Cancellation token honored by long-running policy backends.</param>
    /// <returns>Zero or more <see cref="PolicyFinding"/>s. Empty list (not null) when the policy is satisfied or does not apply.</returns>
    Task<IReadOnlyList<PolicyFinding>> EvaluateAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken);
}
