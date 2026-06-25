using Application.AI.Common.Interfaces.Governance;
using Domain.AI.Governance;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Governance.Classification;

/// <summary>
/// Routes a classification lookup to the Purview surface that can answer for the asset's kind: the
/// Information Protection provider for local files (embedded document labels) and the Data Map provider for
/// scanned cloud data assets (Azure Blob, ADLS Gen2, Azure SQL, Cosmos DB).
/// </summary>
/// <remarks>
/// <para>
/// The two Purview worlds answer for disjoint asset kinds, so a single provider cannot serve both. This
/// router holds each world's provider and dispatches by <see cref="AssetReference.Type"/>. Each world is
/// independently optional: when the matching provider is not wired (the operator enabled only one world,
/// or none), or the asset kind is one Purview cannot classify, the lookup resolves to
/// <see cref="AssetLabelResult.Unknown(AssetReference, DateTimeOffset)"/> without a network call, so the
/// unknown-asset policy applies.
/// </para>
/// <para>
/// Routing is by asset kind alone and incurs no I/O of its own; per-asset result caching is the
/// surrounding <see cref="CachedDataClassificationProvider"/>'s job, so this router is wrapped by it.
/// </para>
/// </remarks>
public sealed class RoutingDataClassificationProvider : IDataClassificationProvider
{
    private readonly IDataClassificationProvider? _informationProtection;
    private readonly IDataClassificationProvider? _dataMap;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RoutingDataClassificationProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="RoutingDataClassificationProvider"/> class.</summary>
    /// <param name="informationProtection">
    /// The Information Protection provider for local-file labels, or null when that world is not wired.
    /// </param>
    /// <param name="dataMap">
    /// The Data Map provider for scanned cloud assets, or null when that world is not wired.
    /// </param>
    /// <param name="timeProvider">Clock used to timestamp Unknown results produced without a provider call.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public RoutingDataClassificationProvider(
        IDataClassificationProvider? informationProtection,
        IDataClassificationProvider? dataMap,
        TimeProvider timeProvider,
        ILogger<RoutingDataClassificationProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _informationProtection = informationProtection;
        _dataMap = dataMap;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AssetLabelResult> GetLabelAsync(AssetReference asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var provider = asset.Type switch
        {
            AssetType.LocalFile => _informationProtection,
            AssetType.AzureBlob or AssetType.AdlsGen2 or AssetType.AzureSql or AssetType.CosmosDb => _dataMap,
            _ => null,
        };

        if (provider is null)
        {
            _logger.LogDebug(
                "No classification provider is wired for asset kind {AssetType}; resolving as Unknown.", asset.Type);
            return Task.FromResult(AssetLabelResult.Unknown(asset, _timeProvider.GetUtcNow()));
        }

        return provider.GetLabelAsync(asset, cancellationToken);
    }
}
