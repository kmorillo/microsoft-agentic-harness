using System.Text.Json;
using Application.AI.Common.Interfaces.Observability;
using Application.AI.Common.Interfaces.Tools;
using Domain.AI.Changes;
using Domain.AI.Models;

namespace Infrastructure.AI.Tools;

/// <summary>
/// Returns the catalog of dashboard metrics (id, title, description, PromQL, chart type, unit,
/// category) as JSON so an agent can choose a valid metric before charting or reasoning about it.
/// </summary>
/// <remarks>
/// <para>
/// A read-only, non-blocking server-side tool. It reads the same curated catalog the dashboard
/// renders via the <see cref="IMetricCatalog"/> seam, so the agent's notion of "available metrics"
/// and the dashboard's panels share one source of truth.
/// </para>
/// <para>
/// Register via keyed DI:
/// <code>
/// services.AddKeyedSingleton&lt;ITool&gt;(ListMetricsTool.ToolName, (sp, _) =&gt;
///     new ListMetricsTool(sp.GetRequiredService&lt;IMetricCatalog&gt;()));
/// </code>
/// </para>
/// </remarks>
public sealed class ListMetricsTool : ITool
{
    /// <summary>The tool name matching keyed DI registration and SKILL.md declarations.</summary>
    public const string ToolName = "list_metrics";

    private const string List = "list";

    private static readonly IReadOnlyList<string> Operations = [List];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IMetricCatalog _catalog;

    /// <summary>Initializes a new instance of the <see cref="ListMetricsTool"/> class.</summary>
    /// <param name="catalog">The shared dashboard metric catalog.</param>
    public ListMetricsTool(IMetricCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public string Name => ToolName;

    /// <inheritdoc />
    public string Description =>
        "Lists the available dashboard metrics (id, title, description, chart type, unit, category) " +
        "so you can pick a valid metric to chart or discuss. Optionally filter by category.";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedOperations => Operations;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public bool IsConcurrencySafe => true;

    /// <inheritdoc />
    public BlastRadius RiskTier => BlastRadius.Trivial;

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        string operation,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(operation, List, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ToolResult.Fail($"Unknown operation: {operation}. Supported: {List}"));

        var category = parameters.TryGetValue("category", out var value) && value is string s && !string.IsNullOrWhiteSpace(s)
            ? s
            : null;

        var entries = category is null
            ? _catalog.Entries
            : _catalog.Entries.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        return Task.FromResult(ToolResult.Ok(json));
    }
}
