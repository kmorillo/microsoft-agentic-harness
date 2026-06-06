namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// The severity of a single <see cref="PolicyFinding"/> reported by an
/// <see cref="IChangeProposalPolicy"/>. The <c>PolicyGate</c> maps findings to a
/// <c>GateResult</c> by comparing the highest finding severity to a configured
/// minimum-to-fail threshold.
/// </summary>
/// <remarks>
/// The ordering is meaningful: every member's integer value is a strict upper bound
/// of the previous member, so range checks like <c>severity &gt;= PolicyFindingSeverity.High</c>
/// are valid.
/// </remarks>
public enum PolicyFindingSeverity
{
    /// <summary>Informational finding. Default threshold treats Info as non-blocking.</summary>
    Info = 0,

    /// <summary>Low-priority concern, typically a stylistic or non-prod issue.</summary>
    Low = 1,

    /// <summary>Notable concern that warrants reviewer attention but does not block by default.</summary>
    Medium = 2,

    /// <summary>Material concern. Default threshold treats High as blocking.</summary>
    High = 3,

    /// <summary>Severe finding. Always blocking regardless of threshold configuration.</summary>
    Critical = 4
}
