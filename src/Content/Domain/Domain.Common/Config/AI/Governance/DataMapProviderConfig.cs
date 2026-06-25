using Domain.Common.Config.Azure;

namespace Domain.Common.Config.AI.Governance;

/// <summary>
/// Configuration for the Microsoft Purview Data Map classification provider — the data-estate half of the
/// data-classification gate. Bound from
/// <c>AppConfig:AI:Governance:DataClassification:DataMap</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Data Map provider resolves a cloud data asset's sensitivity by reading the scanned catalog entry
/// for that asset from the Purview Data Map (the Apache Atlas-based catalog), keyed by the asset's
/// fully-qualified name. It surfaces the asset's applied sensitivity label and the classification findings
/// a scan attached to it (for example "Credit Card Number"). This covers Azure Blob, ADLS Gen2, Azure SQL,
/// and Cosmos DB — the asset kinds the Data Map can scan — whereas the Information Protection provider
/// covers documents/files with embedded labels.
/// </para>
/// <para>
/// <strong>Scan-time metadata.</strong> Data Map labels and classifications reflect the asset's last scan,
/// not its live state, so a result can be stale between scans. The provider flags this on
/// <c>AssetLabelResult.IsStale</c> using <see cref="StalenessThreshold"/> so a policy or audit can
/// distinguish "recently verified" from "no recent scan".
/// </para>
/// <para>
/// <strong>Opt-in.</strong> <see cref="Enabled"/> defaults to <c>false</c>, so even with classification
/// enforcement switched on the harness only contacts the Purview Data Map once an operator deliberately
/// wires it with an account endpoint and credentials.
/// </para>
/// </remarks>
public sealed class DataMapProviderConfig
{
    /// <summary>
    /// Whether the Purview Data Map provider is active. When <c>false</c> (the default) the harness does
    /// not register the provider and never contacts the Data Map.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Base data-plane endpoint of the Purview account's catalog — the host the Atlas REST calls target,
    /// for example <c>https://my-account.purview.azure.com</c>. The Atlas catalog path
    /// (<c>/catalog/api/atlas/v2/…</c>) is appended by the provider. For the new Microsoft Purview portal
    /// the shared endpoint is <c>https://api.purview-service.microsoft.com</c>.
    /// </summary>
    public string AccountEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// OAuth scopes requested when minting the Data Map access token. Defaults to the Purview data-plane
    /// resource scope (<c>https://purview.azure.net/.default</c>), which is correct for a managed identity
    /// or app registration assigned a Purview data-reader role.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = ["https://purview.azure.net/.default"];

    /// <summary>
    /// Entra credential used to authenticate to the Purview Data Map. Leave the explicit fields unset to
    /// use the ambient managed identity / default credential chain (the recommended production posture);
    /// supply client-secret or certificate fields for non-managed-identity hosts. A secret, if any, must
    /// come from User Secrets or Key Vault — never appsettings.json.
    /// </summary>
    public EntraCredentialConfig Auth { get; init; } = new();

    /// <summary>
    /// Optional prefix that marks which of an asset's free-text label tags is its applied sensitivity
    /// label. The Purview Data Map returns an asset's labels as a free-text tag set that mixes the applied
    /// sensitivity label with arbitrary operational tags, and does not flag which is which. When this
    /// prefix is set (matched case-insensitively), only tags beginning with it are treated as the
    /// sensitivity label, and the prefix is stripped from the resolved label name — so an organization that
    /// tags sensitivity as <c>MIP_Confidential</c> sets the prefix to <c>MIP_</c> and authors policy
    /// against <c>Confidential</c>. When empty (the default), every tag is a candidate and the first by
    /// ordinal order is used; deployments whose assets also carry non-sensitivity tags should set a prefix
    /// so the gate does not key its decision on the wrong tag.
    /// </summary>
    public string SensitivityLabelTagPrefix { get; init; } = string.Empty;

    /// <summary>
    /// How old a scanned asset's metadata may be before its result is flagged as stale on
    /// <c>AssetLabelResult.IsStale</c>. Compared against the catalog entry's last-update time. An asset
    /// whose update time is unknown is always treated as stale, since freshness cannot be verified.
    /// Defaults to seven days, a typical Data Map scan cadence.
    /// </summary>
    public TimeSpan StalenessThreshold { get; init; } = TimeSpan.FromDays(7);
}
