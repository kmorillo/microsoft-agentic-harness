using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.AI.Common.Interfaces.Governance;
using Azure.Core;
using Domain.AI.Governance;
using Domain.Common.Config.AI.Governance;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Governance.Classification;

/// <summary>
/// Microsoft Purview Data Map classification provider. Resolves a cloud data asset's sensitivity by
/// reading its scanned catalog entry from the Purview Data Map (the Apache Atlas-based catalog), keyed by
/// the asset's fully-qualified name.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What the Data Map can and cannot do.</strong> The Data Map answers for asset kinds it has
/// scanned — Azure Blob, ADLS Gen2, Azure SQL, Cosmos DB — and returns the applied sensitivity label plus
/// the classification findings a scan attached to the asset. It does not track local files (that is the
/// Information Protection provider's world), and its labels are <em>scan-time metadata</em>: they reflect
/// the last scan, not live state, so a result can be stale between scans. The provider flags this on
/// <see cref="AssetLabelResult.IsStale"/>.
/// </para>
/// <para>
/// <strong>How the sensitivity label is resolved.</strong> The default policy evaluator is
/// sensitivity-label driven — it keys its allow/redact/block decision on the label <em>name</em>, with
/// scan classifications carried only for audit. The Data Map surfaces an asset's applied sensitivity label
/// among the catalog entry's free-text label tags but does not flag which tag it is, so this provider
/// selects the sensitivity-label tag deterministically: when
/// <see cref="DataMapProviderConfig.SensitivityLabelTagPrefix"/> is set, the first (by ordinal order) tag
/// carrying that prefix wins and the prefix is stripped from the resolved name; otherwise the first tag by
/// ordinal order is used. The resolved name doubles as the label id (Atlas label tags carry no GUID), and
/// the scan classifications map to <see cref="DataClassification"/> findings. This surfacing is
/// source-dependent — an asset whose scan propagated no matching label tag resolves with no label and
/// falls to the unknown-asset policy, even when it carries classifications.
/// </para>
/// <para>
/// <strong>Failure handling.</strong> An asset the Data Map has never scanned returns
/// <see cref="HttpStatusCode.NotFound"/>, which the provider maps to a benign Unknown result (there is
/// genuinely nothing to enforce on). Any other non-success status surfaces as an exception rather than a
/// silent Unknown, so the enforcement point can apply its fail-closed policy instead of treating an
/// unreachable backend as "nothing to see". The fully-qualified name is the only request input and the
/// account endpoint is operator-configured, so there is no response-driven URL to follow and no bearer
/// token to leak off-host.
/// </para>
/// </remarks>
public sealed class PurviewDataMapClient : IDataClassificationProvider
{
    /// <summary>
    /// Name of the <see cref="IHttpClientFactory"/> client this provider resolves on each lookup. Using
    /// the factory per call lets it pool and rotate the underlying handler instead of pinning one handler
    /// for the lifetime of this singleton provider (which would leave DNS and sockets stale).
    /// </summary>
    public const string HttpClientName = "purview-data-map";

    /// <summary>
    /// Maps each Data Map-classifiable <see cref="AssetType"/> to its Apache Atlas entity type name, which
    /// the Atlas <c>get entity by unique attribute</c> call needs alongside the qualified name. Azure SQL
    /// maps to the table type; column-level labels are a source-dependent preview and are out of scope.
    /// Asset kinds absent from this map are not answerable by the Data Map and resolve to Unknown.
    /// </summary>
    private static readonly IReadOnlyDictionary<AssetType, string> AtlasTypeNames =
        new Dictionary<AssetType, string>
        {
            [AssetType.AzureBlob] = "azure_blob_path",
            [AssetType.AdlsGen2] = "azure_datalake_gen2_path",
            [AssetType.AzureSql] = "azure_sql_table",
            [AssetType.CosmosDb] = "azure_cosmosdb_sqlapi_collection",
        };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PurviewDataMapClient> _logger;

    private readonly Uri _catalogBase;
    private readonly string[] _scopes;
    private readonly TimeSpan _stalenessThreshold;
    private readonly string _sensitivityLabelTagPrefix;

    /// <summary>Initializes a new instance of the <see cref="PurviewDataMapClient"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to resolve a fresh, handler-rotated client per lookup.</param>
    /// <param name="credential">Entra credential used to mint Purview data-plane access tokens.</param>
    /// <param name="config">The Data Map provider configuration this client binds to.</param>
    /// <param name="timeProvider">Clock used for result timestamps and staleness evaluation.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public PurviewDataMapClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        DataMapProviderConfig config,
        TimeProvider timeProvider,
        ILogger<PurviewDataMapClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _timeProvider = timeProvider;
        _logger = logger;
        _scopes = [.. config.Scopes];
        _stalenessThreshold = config.StalenessThreshold;
        _sensitivityLabelTagPrefix = config.SensitivityLabelTagPrefix ?? string.Empty;
        _catalogBase = BuildCatalogBase(config.AccountEndpoint);
    }

    /// <inheritdoc />
    public async Task<AssetLabelResult> GetLabelAsync(AssetReference asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);

        // The Data Map can only answer for scanned cloud asset kinds, and only when given a qualified name
        // to key on. Anything else has nothing to look up — return Unknown without a network call so the
        // unknown-asset policy applies.
        if (!AtlasTypeNames.TryGetValue(asset.Type, out var typeName) ||
            string.IsNullOrWhiteSpace(asset.Identifier))
        {
            return AssetLabelResult.Unknown(asset, _timeProvider.GetUtcNow());
        }

        var requestUri = BuildEntityUri(typeName, asset.Identifier);
        var entity = await GetEntityAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        // A NotFound asset is genuinely unscanned — there is nothing to enforce on, so treat it as Unknown
        // rather than a failure.
        if (entity is null)
            return AssetLabelResult.Unknown(asset, now);

        var label = ResolveLabel(entity.Labels);
        var classifications = ResolveClassifications(entity.Classifications);
        var isStale = IsStale(entity.UpdateTime, now);

        return new AssetLabelResult(asset, label, classifications, LabelSource.DataMap, now, isStale);
    }

    /// <summary>
    /// Reads the catalog entity, returning null when the asset is unscanned (<see cref="HttpStatusCode.NotFound"/>)
    /// and throwing on any other non-success status so the gate can fail closed.
    /// </summary>
    private async Task<AtlasEntity?> GetEntityAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var token = await _credential
            .GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken)
            .ConfigureAwait(false);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

        // Resolved (and disposed) per request so the factory can rotate the underlying handler.
        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Purview Data Map has no scanned entry for the requested asset; treating it as unclassified.");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            // Status only — never echo the response body or token, which can carry tenant detail.
            throw new InvalidOperationException(
                $"Failed to read the asset's entry from the Microsoft Purview Data Map (HTTP {(int)response.StatusCode}).");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<AtlasEntityWithExtInfo>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        // A 2xx whose body carries no entity is an unrecognized shape — fail closed rather than silently
        // degrading a real asset to Unknown.
        if (payload?.Entity is null)
        {
            throw new InvalidOperationException(
                "The Microsoft Purview Data Map response did not contain an 'entity'; the response shape was not understood.");
        }

        return payload.Entity;
    }

    /// <summary>
    /// Resolves the asset's sensitivity label from its Atlas label tags. Tags are considered in ordinal
    /// order so the same asset always resolves to the same label regardless of the order the Data Map
    /// returns them in. When a prefix is configured, only tags carrying it qualify and the prefix is
    /// stripped from the resolved name; otherwise the first tag is used. The resolved name doubles as the
    /// id, since Data Map label tags carry no GUID. Returns null when no qualifying tag is present, which
    /// the evaluator treats via the unknown-asset policy.
    /// </summary>
    private SensitivityLabel? ResolveLabel(IReadOnlyList<string>? labels)
    {
        if (labels is null)
            return null;

        var candidates = labels
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim());

        // No prefix: the first tag by ordinal order is the label. With a prefix: only prefix-carrying tags
        // qualify and the prefix is stripped. Ordinal ordering keeps the choice stable across scans.
        var name = _sensitivityLabelTagPrefix.Length == 0
            ? candidates.OrderBy(tag => tag, StringComparer.Ordinal).FirstOrDefault()
            : candidates
                .Where(tag => tag.StartsWith(_sensitivityLabelTagPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(tag => tag[_sensitivityLabelTagPrefix.Length..].Trim())
                .Where(stripped => stripped.Length > 0)
                .OrderBy(stripped => stripped, StringComparer.Ordinal)
                .FirstOrDefault();

        return string.IsNullOrEmpty(name) ? null : new SensitivityLabel(name, name);
    }

    /// <summary>
    /// Projects the scan classifications onto <see cref="DataClassification"/> findings, dropping blanks.
    /// Atlas reports no confidence on the entity classification, so the default full confidence is used.
    /// </summary>
    private static IReadOnlyList<DataClassification> ResolveClassifications(IReadOnlyList<AtlasClassification>? classifications)
    {
        if (classifications is null || classifications.Count == 0)
            return [];

        var findings = new List<DataClassification>(classifications.Count);
        foreach (var classification in classifications)
        {
            if (!string.IsNullOrWhiteSpace(classification.TypeName))
                findings.Add(new DataClassification(classification.TypeName.Trim()));
        }

        return findings;
    }

    /// <summary>
    /// Judges staleness from the catalog entry's last-update time. An asset whose update time is unknown
    /// (null or non-positive) or implausibly in the future (clock skew, corrupt scan metadata) is treated
    /// as stale, since in neither case can freshness be trusted; otherwise it is stale once its age exceeds
    /// the configured threshold.
    /// </summary>
    private bool IsStale(long? updateTimeEpochMs, DateTimeOffset now)
    {
        if (updateTimeEpochMs is null or <= 0)
            return true;

        var age = now - DateTimeOffset.FromUnixTimeMilliseconds(updateTimeEpochMs.Value);
        return age < TimeSpan.Zero || age > _stalenessThreshold;
    }

    private static Uri BuildCatalogBase(string accountEndpoint)
    {
        var baseUrl = accountEndpoint.EndsWith('/') ? accountEndpoint : accountEndpoint + "/";
        return new Uri(new Uri(baseUrl), "catalog/api/atlas/v2/entity/uniqueAttribute/type/");
    }

    private Uri BuildEntityUri(string typeName, string qualifiedName) =>
        new(_catalogBase,
            $"{Uri.EscapeDataString(typeName)}?attr:qualifiedName={Uri.EscapeDataString(qualifiedName)}");
}
