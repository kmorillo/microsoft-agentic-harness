namespace Application.AI.Common.Interfaces;

/// <summary>
/// Ambient seam that lets the agent-turn handler push assistant text deltas to the
/// active transport (e.g. a SignalR connection) as the model generates them, without
/// the Application layer taking a dependency on any transport.
/// </summary>
/// <remarks>
/// The transport (the Presentation-layer orchestrator) attaches a sink before
/// dispatching the turn; the handler reads the ambient sink to decide between
/// streaming (<c>RunStreamingAsync</c>) and blocking (<c>RunAsync</c>) execution.
/// When no sink is attached the handler runs blocking, so non-interactive callers
/// (tests, batch jobs) keep their existing behaviour. Mirrors the ambient pattern of
/// <see cref="Services.LlmUsageCapture.Current"/>.
/// </remarks>
public interface IAgentTurnStreamSink
{
    /// <summary>
    /// Emits a single assistant text delta to the attached consumer. Empty deltas are
    /// ignored. Honours <paramref name="cancellationToken"/> so a disconnected consumer
    /// stops the stream promptly.
    /// </summary>
    /// <param name="delta">The newly generated assistant text fragment.</param>
    /// <param name="cancellationToken">Cancels emission when the consumer goes away.</param>
    Task EmitAsync(string delta, CancellationToken cancellationToken);
}
