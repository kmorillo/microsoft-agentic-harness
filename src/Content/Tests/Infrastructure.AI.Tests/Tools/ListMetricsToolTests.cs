using System.Text.Json;
using Application.AI.Common.Interfaces.Observability;
using FluentAssertions;
using Infrastructure.AI.Tools;
using Xunit;

namespace Infrastructure.AI.Tests.Tools;

/// <summary>
/// Tests for <see cref="ListMetricsTool"/> — the read-only tool that surfaces the shared metric catalog
/// to the agent as JSON. Covers full listing, category filtering, and operation validation.
/// </summary>
public sealed class ListMetricsToolTests
{
    private static readonly IMetricCatalog Catalog = new FakeCatalog(
    [
        new MetricDescriptor("tokens_by_model", "Tokens by Model", "d1", "q1", "pie", "tokens", "tokens"),
        new MetricDescriptor("cost_total", "Total Cost", "d2", "q2", "stat", "usd", "cost"),
    ]);

    [Fact]
    public void Metadata_IsReadOnlyAndConcurrencySafe()
    {
        var sut = new ListMetricsTool(Catalog);
        sut.Name.Should().Be("list_metrics");
        sut.IsReadOnly.Should().BeTrue();
        sut.IsConcurrencySafe.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_List_ReturnsAllEntriesAsJson()
    {
        var sut = new ListMetricsTool(Catalog);

        var result = await sut.ExecuteAsync("list", new Dictionary<string, object?>());

        result.Success.Should().BeTrue();
        var entries = JsonSerializer.Deserialize<List<MetricDescriptor>>(result.Output!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        entries.Should().HaveCount(2);
        entries!.Select(e => e.Id).Should().Contain(["tokens_by_model", "cost_total"]);
    }

    [Fact]
    public async Task ExecuteAsync_List_FiltersByCategory()
    {
        var sut = new ListMetricsTool(Catalog);

        var result = await sut.ExecuteAsync("list",
            new Dictionary<string, object?> { ["category"] = "cost" });

        result.Success.Should().BeTrue();
        var entries = JsonSerializer.Deserialize<List<MetricDescriptor>>(result.Output!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        entries.Should().ContainSingle().Which.Id.Should().Be("cost_total");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownOperation_Fails()
    {
        var sut = new ListMetricsTool(Catalog);

        var result = await sut.ExecuteAsync("delete", new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
    }

    private sealed class FakeCatalog(IReadOnlyList<MetricDescriptor> entries) : IMetricCatalog
    {
        public IReadOnlyList<MetricDescriptor> Entries { get; } = entries;
    }
}
