using Application.AI.Common.Interfaces;

namespace Application.AI.Common.Services;

/// <summary>
/// Ambient holder plus delegate-backed implementation of <see cref="IAgentTurnStreamSink"/>.
/// Mirrors the <see cref="LlmUsageCapture.Current"/> <see cref="AsyncLocal{T}"/> pattern: the
/// transport (orchestrator) sets <see cref="Current"/> to a sink wrapping its per-chunk
/// callback before dispatching the turn, and the handler reads <see cref="Current"/> to choose
/// streaming over blocking execution. Flowing the sink ambiently keeps the MediatR command a
/// pure data record (no delegate that would break value-equality or cache keys).
/// </summary>
public sealed class AgentTurnStreamSink : IAgentTurnStreamSink
{
    private static readonly AsyncLocal<IAgentTurnStreamSink?> s_current = new();

    /// <summary>
    /// The sink attached to the current async flow, or <c>null</c> when the turn has no live
    /// consumer (tests, batch jobs). Set by the transport before dispatch; cleared afterward.
    /// </summary>
    public static IAgentTurnStreamSink? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    private readonly Func<string, CancellationToken, Task> _onDelta;

    /// <summary>
    /// Creates a sink that forwards each assistant text delta to <paramref name="onDelta"/>.
    /// </summary>
    /// <param name="onDelta">The transport callback invoked per text delta.</param>
    public AgentTurnStreamSink(Func<string, CancellationToken, Task> onDelta)
    {
        ArgumentNullException.ThrowIfNull(onDelta);
        _onDelta = onDelta;
    }

    /// <inheritdoc />
    public Task EmitAsync(string delta, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(delta) ? Task.CompletedTask : _onDelta(delta, cancellationToken);
}
