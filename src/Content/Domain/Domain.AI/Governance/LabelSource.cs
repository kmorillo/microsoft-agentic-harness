namespace Domain.AI.Governance;

/// <summary>
/// Identifies which Purview surface produced an <see cref="AssetLabelResult"/>, or that no
/// classification was available.
/// </summary>
public enum LabelSource
{
    /// <summary>
    /// No classification was resolved — the asset is unknown to Purview, unscanned, or of a kind that
    /// cannot be classified. Consumers apply the configured unknown-asset policy.
    /// </summary>
    None,

    /// <summary>
    /// The label came from Microsoft Purview Information Protection (Microsoft Graph
    /// <c>informationProtection</c> / an embedded file label) — the document/file labelling world.
    /// </summary>
    InformationProtection,

    /// <summary>
    /// The label/classification came from the Microsoft Purview Data Map — the data-estate world (blob,
    /// ADLS, database columns). These are scan-time metadata and may be stale between scans.
    /// </summary>
    DataMap
}
