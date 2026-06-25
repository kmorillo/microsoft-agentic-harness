namespace Domain.Common.Config.AI.Governance;

/// <summary>
/// The enforcement action a data-classification policy assigns to a tool invocation based on the
/// resolved sensitivity of the asset it touches.
/// </summary>
/// <remarks>
/// Lives in Domain.Common (alongside <c>ThreatLevel</c>) because it is shared by both the
/// configuration layer (<see cref="DataClassificationConfig"/>) and the Domain.AI policy decision.
/// This is the <em>decision</em> the policy computes; whether a <see cref="Block"/> or
/// <see cref="Redact"/> decision is actually applied (versus merely observed and logged) is governed
/// separately by <see cref="ClassificationEnforcementMode"/> at the enforcement point.
/// </remarks>
public enum ClassificationAction
{
    /// <summary>Permit the tool invocation; the asset's sensitivity does not warrant intervention.</summary>
    Allow,

    /// <summary>
    /// Permit the invocation but redact the sensitive content from the result before it re-enters the
    /// model context. Used for labels that are restricted but not prohibited.
    /// </summary>
    Redact,

    /// <summary>
    /// Deny the invocation; the asset's sensitivity prohibits the agent from accessing it. The model
    /// receives a denial message in place of the tool result.
    /// </summary>
    Block
}
