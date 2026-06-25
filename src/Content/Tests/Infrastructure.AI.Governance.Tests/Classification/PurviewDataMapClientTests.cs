using System.Net;
using System.Text;
using Azure.Core;
using Domain.AI.Governance;
using Domain.Common.Config.AI.Governance;
using FluentAssertions;
using Infrastructure.AI.Governance.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Governance.Tests.Classification;

/// <summary>
/// Tests for <see cref="PurviewDataMapClient"/> against a stubbed Apache Atlas catalog endpoint. They pin
/// the honest contract of the Data Map provider: it reads a scanned cloud asset by qualified name, maps the
/// applied label tag to a sensitivity label and the scan classifications to findings, judges staleness from
/// the entry's update time, treats an unscanned asset (404) as a benign Unknown, and fails closed on any
/// other backend error without leaking the access token.
/// </summary>
public sealed class PurviewDataMapClientTests
{
    private const string BlobQn = "https://acct.blob.core.windows.net/container/customers.csv";
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private static DataMapProviderConfig Config(TimeSpan? staleness = null, string? labelPrefix = null) => new()
    {
        Enabled = true,
        AccountEndpoint = "https://acct.purview.azure.com",
        Scopes = ["https://purview.azure.net/.default"],
        StalenessThreshold = staleness ?? TimeSpan.FromDays(7),
        SensitivityLabelTagPrefix = labelPrefix ?? string.Empty,
    };

    private static PurviewDataMapClient CreateClient(
        StubHandler handler, MutableTimeProvider time, TimeSpan? staleness = null, string? labelPrefix = null) =>
        new(new StubHttpClientFactory(handler), new FakeCredential(), Config(staleness, labelPrefix), time,
            NullLogger<PurviewDataMapClient>.Instance);

    private static AssetReference Blob(string qn = BlobQn) => new(AssetType.AzureBlob, qn);

    [Fact]
    public async Task GetLabelAsync_ScannedAsset_MapsLabelAndClassifications()
    {
        // updateTime is "now", so well within the freshness window.
        var entity = EntityJson(
            labels: ["Confidential"],
            classifications: ["MICROSOFT.GOVERNMENT.US.SOCIAL_SECURITY_NUMBER", "MICROSOFT.FINANCIAL.CREDIT_CARD_NUMBER"],
            updateTimeMs: Now.ToUnixTimeMilliseconds());
        var sut = CreateClient(new StubHandler(_ => Ok(entity)), new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.Source.Should().Be(LabelSource.DataMap);
        result.Label!.Name.Should().Be("Confidential");
        result.Label.Id.Should().Be("Confidential", "Atlas label tags carry no GUID, so the name doubles as the id");
        result.Classifications.Select(c => c.Name).Should().BeEquivalentTo(
            "MICROSOFT.GOVERNMENT.US.SOCIAL_SECURITY_NUMBER", "MICROSOFT.FINANCIAL.CREDIT_CARD_NUMBER");
        result.IsStale.Should().BeFalse("the asset was scanned within the staleness threshold");
    }

    [Fact]
    public async Task GetLabelAsync_TargetsAtlasGetByUniqueAttribute_WithEncodedQualifiedName()
    {
        var handler = new StubHandler(_ => Ok(EntityJson(labels: ["Confidential"])));
        var sut = CreateClient(handler, new MutableTimeProvider(Now));

        await sut.GetLabelAsync(Blob(), CancellationToken.None);

        var uri = handler.LastRequestUri!;
        uri.AbsolutePath.Should().Be(
            "/catalog/api/atlas/v2/entity/uniqueAttribute/type/azure_blob_path");
        // The qualified name (a URL with ':' and '/') must be percent-encoded into the query.
        uri.Query.Should().Contain("attr:qualifiedName=");
        uri.Query.Should().Contain(Uri.EscapeDataString(BlobQn));
        handler.LastAuthScheme.Should().Be("Bearer");
        handler.LastAuthToken.Should().Be(FakeCredential.TokenValue);
    }

    [Theory]
    [InlineData(AssetType.AzureBlob, "azure_blob_path")]
    [InlineData(AssetType.AdlsGen2, "azure_datalake_gen2_path")]
    [InlineData(AssetType.AzureSql, "azure_sql_table")]
    [InlineData(AssetType.CosmosDb, "azure_cosmosdb_sqlapi_collection")]
    public async Task GetLabelAsync_RoutesEachCloudKind_ToItsAtlasTypeName(AssetType type, string expectedType)
    {
        var handler = new StubHandler(_ => Ok(EntityJson(labels: ["Confidential"])));
        var sut = CreateClient(handler, new MutableTimeProvider(Now));

        await sut.GetLabelAsync(new AssetReference(type, "qn://asset"), CancellationToken.None);

        handler.LastRequestUri!.AbsolutePath.Should().EndWith("/type/" + expectedType);
    }

    [Fact]
    public async Task GetLabelAsync_StaleScan_FlagsIsStale()
    {
        // Last updated 30 days ago, past the 7-day threshold.
        var entity = EntityJson(labels: ["Confidential"], updateTimeMs: Now.AddDays(-30).ToUnixTimeMilliseconds());
        var sut = CreateClient(new StubHandler(_ => Ok(entity)), new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.IsStale.Should().BeTrue("a scan older than the staleness threshold is not known to be current");
    }

    [Fact]
    public async Task GetLabelAsync_FutureUpdateTime_TreatedAsStale()
    {
        // A timestamp ahead of now (clock skew or corrupt scan metadata) cannot be trusted, so the result
        // is conservatively stale rather than reported as freshly verified.
        var entity = EntityJson(labels: ["Confidential"], updateTimeMs: Now.AddHours(6).ToUnixTimeMilliseconds());
        var sut = CreateClient(new StubHandler(_ => Ok(entity)), new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetLabelAsync_MissingUpdateTime_TreatedAsStale()
    {
        // No updateTime means freshness cannot be verified, so the result is conservatively stale.
        var sut = CreateClient(new StubHandler(_ => Ok(EntityJson(labels: ["Confidential"]))), new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetLabelAsync_NoLabelTagButClassifications_ReturnsClassificationsWithNoLabel()
    {
        // An asset can be classified by a scan yet carry no applied sensitivity label tag. The label is
        // null (so the unknown-asset policy applies), but the findings are still surfaced for audit.
        var entity = EntityJson(labels: null, classifications: ["MICROSOFT.FINANCIAL.CREDIT_CARD_NUMBER"]);
        var sut = CreateClient(new StubHandler(_ => Ok(entity)), new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.Label.Should().BeNull();
        result.Source.Should().Be(LabelSource.DataMap);
        result.Classifications.Should().ContainSingle(c => c.Name == "MICROSOFT.FINANCIAL.CREDIT_CARD_NUMBER");
    }

    [Fact]
    public async Task GetLabelAsync_BlankAndWhitespaceLabelTags_AreSkipped()
    {
        var entity = EntityJson(labels: ["  ", "", "Highly Confidential"]);
        var sut = CreateClient(new StubHandler(_ => Ok(entity)), new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.Label!.Name.Should().Be("Highly Confidential", "blank tags are skipped, the first real tag wins");
    }

    [Fact]
    public async Task GetLabelAsync_MultipleTagsNoPrefix_PicksDeterministicallyByOrdinalOrder()
    {
        // The Data Map returns label tags as an unordered set. With no prefix configured the provider must
        // still resolve the same tag every time, so ordinal order decides rather than response order.
        var first = EntityJson(labels: ["Internal", "Confidential"]);
        var second = EntityJson(labels: ["Confidential", "Internal"]);
        var sutFirst = CreateClient(new StubHandler(_ => Ok(first)), new MutableTimeProvider(Now));
        var sutSecond = CreateClient(new StubHandler(_ => Ok(second)), new MutableTimeProvider(Now));

        var a = await sutFirst.GetLabelAsync(Blob(), CancellationToken.None);
        var b = await sutSecond.GetLabelAsync(Blob(), CancellationToken.None);

        a.Label!.Name.Should().Be("Confidential", "'Confidential' sorts before 'Internal' by ordinal order");
        b.Label!.Name.Should().Be(a.Label.Name, "response order must not change the resolved label");
    }

    [Fact]
    public async Task GetLabelAsync_PrefixConfigured_SelectsPrefixedTagAndStripsPrefix()
    {
        // Operational tags coexist with the sensitivity tag; the prefix disambiguates which is the label,
        // and the prefix is stripped so policy is authored against the bare label name.
        var entity = EntityJson(labels: ["project-phoenix", "archive", "MIP_Highly Confidential"]);
        var sut = CreateClient(new StubHandler(_ => Ok(entity)), new MutableTimeProvider(Now), labelPrefix: "MIP_");

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.Label!.Name.Should().Be("Highly Confidential");
        result.Label.Id.Should().Be("Highly Confidential");
    }

    [Fact]
    public async Task GetLabelAsync_PrefixConfiguredButNoTagMatches_ResolvesNoLabel()
    {
        // Without a prefixed tag the asset carries no recognizable sensitivity label, so it falls to the
        // unknown-asset policy rather than keying the decision on an arbitrary operational tag.
        var entity = EntityJson(labels: ["project-phoenix", "archive"]);
        var sut = CreateClient(new StubHandler(_ => Ok(entity)), new MutableTimeProvider(Now), labelPrefix: "MIP_");

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.Label.Should().BeNull();
        result.Source.Should().Be(LabelSource.DataMap);
    }

    [Fact]
    public async Task GetLabelAsync_UnscannedAsset_404_ReturnsUnknownWithoutThrowing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateClient(handler, new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(Blob(), CancellationToken.None);

        result.Source.Should().Be(LabelSource.None);
        result.Label.Should().BeNull();
        result.HasClassification.Should().BeFalse();
    }

    [Fact]
    public async Task GetLabelAsync_BackendError_ThrowsWithoutLeakingToken()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateClient(handler, new MutableTimeProvider(Now));

        var act = async () => await sut.GetLabelAsync(Blob(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("500");
        ex.Which.Message.Should().NotContain(FakeCredential.TokenValue);
    }

    [Fact]
    public async Task GetLabelAsync_200WithoutEntity_Throws()
    {
        // A 200 whose body carries no 'entity' is an unrecognized shape — fail closed rather than silently
        // degrading a real asset to Unknown.
        var sut = CreateClient(new StubHandler(_ => Ok("{}")), new MutableTimeProvider(Now));

        var act = async () => await sut.GetLabelAsync(Blob(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetLabelAsync_NonDataMapAssetKind_ReturnsUnknownWithoutCall()
    {
        var handler = new StubHandler(_ => Ok(EntityJson(labels: ["Confidential"])));
        var sut = CreateClient(handler, new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(new AssetReference(AssetType.LocalFile, @"C:\x.txt"), CancellationToken.None);

        result.Source.Should().Be(LabelSource.None);
        handler.CallCount.Should().Be(0, "the Data Map does not track local files, so no round trip is made");
    }

    [Fact]
    public async Task GetLabelAsync_BlankIdentifier_ReturnsUnknownWithoutCall()
    {
        var handler = new StubHandler(_ => Ok(EntityJson(labels: ["Confidential"])));
        var sut = CreateClient(handler, new MutableTimeProvider(Now));

        var result = await sut.GetLabelAsync(new AssetReference(AssetType.AzureBlob, "   "), CancellationToken.None);

        result.Source.Should().Be(LabelSource.None);
        handler.CallCount.Should().Be(0, "with no qualified name there is nothing to look up");
    }

    [Fact]
    public async Task GetLabelAsync_RequestsConfiguredScopes()
    {
        var credential = new FakeCredential();
        var sut = new PurviewDataMapClient(
            new StubHttpClientFactory(new StubHandler(_ => Ok(EntityJson(labels: ["Confidential"])))),
            credential, Config(), new MutableTimeProvider(Now), NullLogger<PurviewDataMapClient>.Instance);

        await sut.GetLabelAsync(Blob(), CancellationToken.None);

        credential.LastScopes.Should().Equal("https://purview.azure.net/.default");
    }

    [Fact]
    public async Task GetLabelAsync_NullAsset_Throws()
    {
        var sut = CreateClient(new StubHandler(_ => Ok(EntityJson(labels: ["Confidential"]))), new MutableTimeProvider(Now));

        var act = async () => await sut.GetLabelAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static string EntityJson(
        IReadOnlyList<string>? labels = null,
        IReadOnlyList<string>? classifications = null,
        long? updateTimeMs = null)
    {
        var labelJson = labels is null ? "null" : "[" + string.Join(",", labels.Select(l => $"\"{l}\"")) + "]";
        var classJson = classifications is null
            ? "null"
            : "[" + string.Join(",", classifications.Select(c => $"{{ \"typeName\": \"{c}\" }}")) + "]";
        var updateJson = updateTimeMs is null ? "null" : updateTimeMs.Value.ToString();
        return $$"""
            {
              "entity": {
                "labels": {{labelJson}},
                "classifications": {{classJson}},
                "updateTime": {{updateJson}}
              }
            }
            """;
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthScheme { get; private set; }
        public string? LastAuthToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastAuthScheme = request.Headers.Authorization?.Scheme;
            LastAuthToken = request.Headers.Authorization?.Parameter;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public const string TokenValue = "fake-data-map-token";
        public string[] LastScopes { get; private set; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            LastScopes = requestContext.Scopes;
            return new AccessToken(TokenValue, Now.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
