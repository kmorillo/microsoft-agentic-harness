namespace Application.AI.Common.Interfaces.Tools;

/// <summary>
/// A bidirectional channel that lets a server-side tool delegate an action to the connected
/// client UI mid-run and block until the client returns a result.
/// </summary>
/// <remarks>
/// <para>
/// This is the framework-independent seam behind the AG-UI "blocking proxy" pattern: a tool such
/// as <c>dashboard_control</c> calls <see cref="InvokeAsync"/>, the transport implementation emits
/// the appropriate client-tool-call protocol events on the live run, parks awaiting the client's
/// out-of-band reply, and resumes the same run with the result. The tool stays in the Infrastructure
/// layer and depends only on this abstraction — it never references the AG-UI/SSE transport types,
/// preserving Clean Architecture's dependency direction.
/// </para>
/// <para>
/// When no client is attached (<see cref="IsClientAttached"/> is <c>false</c>), <see cref="InvokeAsync"/>
/// throws <see cref="InvalidOperationException"/>; tools should surface that as a tool failure rather
/// than letting it bubble. Implementations are expected to enforce a bounded timeout and to honor the
/// supplied <see cref="System.Threading.CancellationToken"/> so a disconnected client never parks a run.
/// </para>
/// </remarks>
public interface IClientToolBridge
{
    /// <summary>
    /// Gets whether a client UI is currently attached to the active run and able to service a
    /// round-trip tool call. <c>false</c> when the tool is invoked outside an interactive client run.
    /// </summary>
    bool IsClientAttached { get; }

    /// <summary>
    /// Delegates a tool invocation to the connected client and awaits its result.
    /// </summary>
    /// <param name="toolName">The client-facing tool name (e.g. <c>dashboard_control</c>).</param>
    /// <param name="argumentsJson">The tool arguments serialized as a JSON object.</param>
    /// <param name="cancellationToken">Cancelled when the owning run is cancelled.</param>
    /// <returns>The client-supplied result payload (typically a short JSON or text summary).</returns>
    /// <exception cref="InvalidOperationException">No client is attached to the active run.</exception>
    /// <exception cref="TimeoutException">The client did not respond within the bounded timeout.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled before a result arrived.</exception>
    Task<string> InvokeAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default);
}
