using Domain.AI.Changes;

namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// Derives the default ordered list of gate keys for a proposal from its
/// <see cref="ChangeTarget.Kind"/> and its <see cref="BlastRadius"/>. Called by
/// <c>SubmitChangeProposalCommand</c> when the caller does not explicitly supply
/// <c>RequiredGates</c>.
/// </summary>
/// <remarks>
/// <para>
/// The default map ships with sensible defaults — typically <c>self_validation →
/// policy → approval → merge</c> for any non-trivial target — but a consumer can
/// register a different <see cref="IChangeProposalGateResolver"/> implementation
/// to enforce stricter pipelines (an extra <c>compliance</c> gate for regulated
/// environments, or an extra <c>cost</c> gate for IaC at high blast radius).
/// </para>
/// <para>
/// The resolver is the right place to enforce "Critical blast radius always
/// requires Approval even when the configured autonomy tier would auto-approve"
/// because the resolver runs before the orchestrator and the resulting gate list
/// is baked into the proposal's <c>RequiredGates</c> — it cannot be lowered later.
/// </para>
/// </remarks>
public interface IChangeProposalGateResolver
{
    /// <summary>
    /// Compute the ordered gate-key list for a proposal with the given target kind
    /// and blast radius.
    /// </summary>
    /// <param name="targetKind">The proposal's target kind.</param>
    /// <param name="blastRadius">The proposal's estimated blast radius.</param>
    /// <returns>An ordered list of gate keys to be assigned to <c>ChangeProposal.RequiredGates</c>. Must not be empty.</returns>
    IReadOnlyList<string> Resolve(ChangeTargetKind targetKind, BlastRadius blastRadius);
}
