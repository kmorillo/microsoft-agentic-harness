using System.Text.Json;
using Application.AI.Common.Interfaces.Tools;
using Domain.AI.Changes;
using Domain.AI.Models;

namespace Infrastructure.AI.Tools;

/// <summary>
/// Lets an agent act on the connected dashboard UI — read the current view, change the time range,
/// navigate, or refresh data — by delegating each operation to the browser through an
/// <see cref="IClientToolBridge"/> round-trip and returning the browser's result to the model.
/// </summary>
/// <remarks>
/// <para>
/// This is a server-defined, governed action set (deliberately not a browser-advertised dynamic tool):
/// the operations are fixed and validated here, while the actual effect is carried out by the client
/// that owns the dashboard state. The tool blocks for the duration of one client round-trip; the
/// <see cref="IClientToolBridge"/> enforces the bounded timeout and honors run cancellation.
/// </para>
/// <para>
/// Register via keyed DI:
/// <code>
/// services.AddKeyedSingleton&lt;ITool&gt;(DashboardControlTool.ToolName, (sp, _) =&gt;
///     new DashboardControlTool(sp.GetRequiredService&lt;IClientToolBridge&gt;()));
/// </code>
/// </para>
/// </remarks>
public sealed class DashboardControlTool : ITool
{
    /// <summary>The tool name matching keyed DI registration and SKILL.md declarations.</summary>
    public const string ToolName = "dashboard_control";

    private const string GetState = "get_state";
    private const string SetTimeRange = "set_time_range";
    private const string Navigate = "navigate";
    private const string RefreshData = "refresh_data";

    private static readonly IReadOnlyList<string> Operations =
        [GetState, SetTimeRange, Navigate, RefreshData];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IClientToolBridge _bridge;

    /// <summary>Initializes a new instance of the <see cref="DashboardControlTool"/> class.</summary>
    /// <param name="bridge">The client round-trip bridge used to delegate operations to the browser.</param>
    public DashboardControlTool(IClientToolBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    /// <inheritdoc />
    public string Name => ToolName;

    /// <inheritdoc />
    public string Description =>
        "Acts on the user's live dashboard: read the current view (get_state), change the time range " +
        "(set_time_range with a preset like '24h'/'7d' or a custom from/to), navigate to a page " +
        "(navigate with a path), or refresh the current data (refresh_data). The action runs in the " +
        "user's browser and returns a short result.";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedOperations => Operations;

    /// <summary><c>get_state</c> only reads; the tool as a whole can change the view, so it is not globally read-only.</summary>
    public BlastRadius RiskTier => BlastRadius.Low;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        string operation,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var op = operation?.ToLowerInvariant();
        if (op is null || !Operations.Contains(op))
            return ToolResult.Fail($"Unknown operation: {operation}. Supported: {string.Join(", ", Operations)}");

        if (!_bridge.IsClientAttached)
            return ToolResult.Fail("No dashboard client is connected to this conversation, so the view cannot be controlled.");

        var argumentsJson = JsonSerializer.Serialize(
            new DashboardControlPayload(op, parameters), SerializerOptions);

        try
        {
            var result = await _bridge.InvokeAsync(ToolName, argumentsJson, cancellationToken);
            return ToolResult.Ok(result);
        }
        catch (TimeoutException)
        {
            return ToolResult.Fail("The dashboard did not respond in time; the action may not have been applied.");
        }
        catch (InvalidOperationException ex)
        {
            // Raised by the bridge when no client is attached (race after the IsClientAttached check).
            return ToolResult.Fail(ex.Message);
        }
        // OperationCanceledException is intentionally NOT caught: a cancelled run must unwind.
    }

    /// <summary>The wire payload posted to the browser: the operation plus its raw parameters.</summary>
    private sealed record DashboardControlPayload(
        string Operation,
        IReadOnlyDictionary<string, object?> Parameters);
}
