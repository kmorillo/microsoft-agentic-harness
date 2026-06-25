using Application.AI.Common.Interfaces.Governance;
using Domain.AI.Governance;
using FluentAssertions;
using Infrastructure.AI.Governance.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Governance.Tests.Classification;

/// <summary>
/// Tests for <see cref="RoutingDataClassificationProvider"/>. They prove the router dispatches each asset
/// kind to the correct Purview world, resolves to Unknown without a provider call when the matching world
/// is not wired or the kind is unclassifiable, and never crosses the streams (a cloud asset never reaches
/// the Information Protection provider, and vice versa).
/// </summary>
public sealed class RoutingDataClassificationProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AssetType.AzureBlob)]
    [InlineData(AssetType.AdlsGen2)]
    [InlineData(AssetType.AzureSql)]
    [InlineData(AssetType.CosmosDb)]
    public async Task GetLabelAsync_CloudAssetKinds_RouteToDataMap(AssetType type)
    {
        var ip = new SpyProvider(LabelSource.InformationProtection);
        var dataMap = new SpyProvider(LabelSource.DataMap);
        var sut = Create(ip, dataMap);

        var result = await sut.GetLabelAsync(new AssetReference(type, "qn://asset"), CancellationToken.None);

        result.Source.Should().Be(LabelSource.DataMap);
        dataMap.Calls.Should().Be(1);
        ip.Calls.Should().Be(0, "a cloud asset must never reach the Information Protection provider");
    }

    [Fact]
    public async Task GetLabelAsync_LocalFile_RoutesToInformationProtection()
    {
        var ip = new SpyProvider(LabelSource.InformationProtection);
        var dataMap = new SpyProvider(LabelSource.DataMap);
        var sut = Create(ip, dataMap);

        var result = await sut.GetLabelAsync(new AssetReference(AssetType.LocalFile, @"C:\x.txt"), CancellationToken.None);

        result.Source.Should().Be(LabelSource.InformationProtection);
        ip.Calls.Should().Be(1);
        dataMap.Calls.Should().Be(0, "a local file must never reach the Data Map provider");
    }

    [Fact]
    public async Task GetLabelAsync_UnknownKind_ReturnsUnknownWithoutCallingEitherProvider()
    {
        var ip = new SpyProvider(LabelSource.InformationProtection);
        var dataMap = new SpyProvider(LabelSource.DataMap);
        var sut = Create(ip, dataMap);

        var result = await sut.GetLabelAsync(AssetReference.Unknown("mcp://output"), CancellationToken.None);

        result.Source.Should().Be(LabelSource.None);
        ip.Calls.Should().Be(0);
        dataMap.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetLabelAsync_DataMapWorldNotWired_CloudAssetResolvesUnknown()
    {
        // Only the Information Protection world is enabled; a cloud asset has no provider and falls through.
        var ip = new SpyProvider(LabelSource.InformationProtection);
        var sut = Create(ip, dataMap: null);

        var result = await sut.GetLabelAsync(new AssetReference(AssetType.AzureBlob, "qn://b"), CancellationToken.None);

        result.Source.Should().Be(LabelSource.None);
        ip.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetLabelAsync_InformationProtectionWorldNotWired_LocalFileResolvesUnknown()
    {
        var dataMap = new SpyProvider(LabelSource.DataMap);
        var sut = Create(ip: null, dataMap);

        var result = await sut.GetLabelAsync(new AssetReference(AssetType.LocalFile, @"C:\x.txt"), CancellationToken.None);

        result.Source.Should().Be(LabelSource.None);
        dataMap.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetLabelAsync_NullAsset_Throws()
    {
        var sut = Create(new SpyProvider(LabelSource.InformationProtection), new SpyProvider(LabelSource.DataMap));

        var act = async () => await sut.GetLabelAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static RoutingDataClassificationProvider Create(
        IDataClassificationProvider? ip, IDataClassificationProvider? dataMap) =>
        new(ip, dataMap, new FixedTimeProvider(Now), NullLogger<RoutingDataClassificationProvider>.Instance);

    private sealed class SpyProvider(LabelSource source) : IDataClassificationProvider
    {
        public int Calls { get; private set; }

        public Task<AssetLabelResult> GetLabelAsync(AssetReference asset, CancellationToken cancellationToken)
        {
            Calls++;
            var label = new SensitivityLabel("id", "name");
            return Task.FromResult(new AssetLabelResult(asset, label, [], source, Now));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
