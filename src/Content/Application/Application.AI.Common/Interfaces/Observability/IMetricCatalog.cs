namespace Application.AI.Common.Interfaces.Observability;

/// <summary>
/// Read-only access to the curated catalog of dashboard metric panels, exposed as a
/// framework-independent seam so tools and services in any layer can enumerate the valid
/// metrics without depending on the Presentation-layer dashboard DTOs.
/// </summary>
/// <remarks>
/// The authoritative catalog data lives with the dashboard it drives (Presentation layer); this
/// interface projects it to a neutral <see cref="MetricDescriptor"/> shape. There is a single
/// source of truth — the implementation reads the same catalog the dashboard renders, so a tool
/// that lists metrics and the dashboard that displays them can never drift apart.
/// </remarks>
public interface IMetricCatalog
{
    /// <summary>Gets all curated metric panel descriptors, ordered as the dashboard groups them.</summary>
    IReadOnlyList<MetricDescriptor> Entries { get; }
}

/// <summary>
/// A neutral description of one curated metric panel: enough for an agent to choose a metric and
/// reason about it, without the Presentation-specific rendering fields.
/// </summary>
/// <param name="Id">Unique identifier (e.g. <c>"tokens_by_model"</c>).</param>
/// <param name="Title">Human-readable panel title.</param>
/// <param name="Description">Brief description of what the metric shows.</param>
/// <param name="Query">The PromQL query backing the metric.</param>
/// <param name="ChartType">Recommended chart type (e.g. <c>"stat"</c>, <c>"timeseries"</c>, <c>"pie"</c>).</param>
/// <param name="Unit">Display unit (e.g. <c>"tokens"</c>, <c>"usd"</c>, <c>"ms"</c>, <c>"percent"</c>).</param>
/// <param name="Category">Dashboard grouping (e.g. <c>"overview"</c>, <c>"tokens"</c>, <c>"cost"</c>).</param>
public sealed record MetricDescriptor(
    string Id,
    string Title,
    string Description,
    string Query,
    string ChartType,
    string Unit,
    string Category);
