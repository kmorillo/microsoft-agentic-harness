using Application.AI.Common.Interfaces.Observability;
using Presentation.AgentHub.Controllers;

namespace Presentation.AgentHub.AgUi;

/// <summary>
/// Presentation-layer implementation of <see cref="IMetricCatalog"/> that projects the dashboard's
/// own curated <see cref="MetricCatalog"/> entries to the neutral <see cref="MetricDescriptor"/> shape.
/// </summary>
/// <remarks>
/// This keeps a single source of truth: the dashboard renders <see cref="MetricCatalog.Entries"/> and
/// the agent's <c>list_metrics</c> tool reads the very same list through this provider, so the two can
/// never drift. The projection is computed once and cached for the provider's singleton lifetime.
/// </remarks>
public sealed class MetricCatalogProvider : IMetricCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<MetricDescriptor> Entries { get; } =
        MetricCatalog.Entries
            .Select(e => new MetricDescriptor(
                e.Id, e.Title, e.Description, e.Query, e.ChartType, e.Unit, e.Category))
            .ToList();
}
