namespace Domain.AI.Governance;

/// <summary>
/// The kind of external asset a tool invocation touches, used to route a
/// <see cref="AssetReference"/> to the data-classification provider that can resolve its
/// sensitivity label.
/// </summary>
/// <remarks>
/// Purview is not omniscient: only a subset of asset kinds can be classified, and the rest resolve to
/// <see cref="Unknown"/> and fall to the configured unknown-asset policy. The classification provider
/// uses this enum to decide which backend (Microsoft Purview Information Protection vs the Purview Data
/// Map) — if any — can answer for the asset.
/// </remarks>
public enum AssetType
{
    /// <summary>
    /// The asset could not be identified, or is a kind Purview cannot classify (a local file with no
    /// embedded label, an unsupported database engine, arbitrary MCP server output). Resolves to the
    /// configured unknown-asset action rather than a real label lookup.
    /// </summary>
    Unknown,

    /// <summary>
    /// A file on the local filesystem. Classifiable only via an embedded Microsoft Purview Information
    /// Protection label read from the file itself; the Purview Data Map does not track local files.
    /// </summary>
    LocalFile,

    /// <summary>An Azure Blob Storage blob, classifiable via the Purview Data Map when scanned.</summary>
    AzureBlob,

    /// <summary>An Azure Data Lake Storage Gen2 path, classifiable via the Purview Data Map when scanned.</summary>
    AdlsGen2,

    /// <summary>
    /// An Azure SQL table or column. Column-level sensitivity labels are available through the Purview
    /// Data Map in preview, and are source-dependent.
    /// </summary>
    AzureSql,

    /// <summary>An Azure Cosmos DB container or item, classifiable via the Purview Data Map when scanned.</summary>
    CosmosDb
}
