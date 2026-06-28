using Application.AI.Common.Interfaces.Tools;
using Microsoft.Extensions.Options;
using Presentation.AgentHub.Config;

namespace Presentation.AgentHub.AgUi;

/// <summary>
/// AG-UI implementation of <see cref="IClientToolBridge"/>: the "blocking proxy" that turns a
/// server-side tool invocation into a mid-run client round-trip over the live SSE stream.
/// </summary>
/// <remarks>
/// <para>
/// The owning <see cref="AgUiRunHandler"/> stores the run's <see cref="IAgUiEventWriter"/> in an
/// <see cref="IAgUiEventWriterAccessor"/> (an <c>AsyncLocal</c>) before dispatching the agent turn.
/// Because the tool executes deep inside that same async context, this bridge reads the ambient
/// writer to emit <see cref="ToolCallStartEvent"/>/<see cref="ToolCallArgsEvent"/>/<see cref="ToolCallEndEvent"/>
/// frames, then parks on the <see cref="PendingToolCallRegistry"/> until the browser posts the result
/// to <c>POST /ag-ui/tool-result</c> — all within the one run.
/// </para>
/// <para>
/// Registered as a singleton: it holds no per-run state of its own (the writer is ambient and the
/// pending map lives in the singleton registry), so a single instance safely serves concurrent runs.
/// </para>
/// </remarks>
public sealed class AgUiClientToolBridge : IClientToolBridge
{
    private readonly IAgUiEventWriterAccessor _writerAccessor;
    private readonly PendingToolCallRegistry _registry;
    private readonly IOptionsMonitor<AgentHubConfig> _config;

    /// <summary>Initializes a new <see cref="AgUiClientToolBridge"/>.</summary>
    public AgUiClientToolBridge(
        IAgUiEventWriterAccessor writerAccessor,
        PendingToolCallRegistry registry,
        IOptionsMonitor<AgentHubConfig> config)
    {
        _writerAccessor = writerAccessor;
        _registry = registry;
        _config = config;
    }

    /// <inheritdoc />
    public bool IsClientAttached => _writerAccessor.Writer is not null;

    /// <inheritdoc />
    public async Task<string> InvokeAsync(
        string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var writer = _writerAccessor.Writer
            ?? throw new InvalidOperationException(
                "No AG-UI client is attached to the current run; a client round-trip tool cannot be used here.");

        var callId = Guid.NewGuid().ToString("N");
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _config.CurrentValue.ClientToolTimeoutSeconds));

        // Register before emitting events so a result can never race ahead of the pending entry.
        var resultTask = _registry.RegisterAsync(callId, timeout, cancellationToken);

        await writer.WriteAsync(new ToolCallStartEvent(callId, toolName), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(new ToolCallArgsEvent(callId, argumentsJson ?? "{}"), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(new ToolCallEndEvent(callId), cancellationToken).ConfigureAwait(false);

        return await resultTask.ConfigureAwait(false);
    }
}
