namespace Domain.AI.Governance;

/// <summary>
/// A Microsoft Purview sensitivity label resolved for an asset (for example "Confidential" or "Highly
/// Confidential\Internal"). Immutable value object.
/// </summary>
/// <param name="Id">The label's stable GUID, as defined in the organization's Purview label taxonomy.</param>
/// <param name="Name">
/// The human-readable label name used to match against the policy's label→action map.
/// </param>
public sealed record SensitivityLabel(
    string Id,
    string Name);
