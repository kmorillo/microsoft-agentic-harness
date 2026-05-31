using Domain.AI.Prompts;

namespace Application.AI.Common.CQRS.Prompts.ReplayTraceWithPromptVersion;

/// <summary>
/// Output of <see cref="ReplayTraceWithPromptVersionCommand"/>: the original
/// (historical) prompt + its rendered body alongside the target version's
/// rendered body and the LLM output produced under temperature-zero replay.
/// </summary>
/// <remarks>
/// <para>
/// Trace replay answers: "if I had been using prompt version <i>Y</i> instead of
/// <i>X</i> when this case ran, what would the assistant have produced?" The
/// caller compares <see cref="TargetOutput"/> against the original output they
/// already have on hand (in their eval report, log, or trace store).
/// </para>
/// <para>
/// Replay is forced to <c>temperature = 0</c> regardless of the original call's
/// temperature so the only delta between two replays is the prompt body change.
/// Without this, observed differences could be sampling noise rather than prompt
/// semantics.
/// </para>
/// </remarks>
public sealed record PromptReplayResult
{
    /// <summary>The trace id that was replayed.</summary>
    public required string TraceId { get; init; }

    /// <summary>The prompt registry name that was swapped.</summary>
    public required string PromptName { get; init; }

    /// <summary>
    /// Descriptor for the version that was actually used at the original trace time,
    /// re-resolved from the registry at replay time. Carries the same
    /// <see cref="PromptDescriptor.ContentHash"/> as the historical record only when
    /// the file on disk has not changed since.
    /// </summary>
    public required PromptDescriptor OriginalDescriptor { get; init; }

    /// <summary>
    /// Descriptor for the requested target version, freshly resolved from the registry.
    /// </summary>
    public required PromptDescriptor TargetDescriptor { get; init; }

    /// <summary>
    /// Body of the original prompt with the caller-supplied variables substituted, so
    /// the caller can A/B-compare wording against the target render even when they
    /// don't replay the original.
    /// </summary>
    public required RenderedPrompt OriginalRenderedPrompt { get; init; }

    /// <summary>Body of the target prompt with the same caller-supplied variables substituted.</summary>
    public required RenderedPrompt TargetRenderedPrompt { get; init; }

    /// <summary>The LLM output produced by sending <see cref="TargetRenderedPrompt"/> at temperature 0.</summary>
    public required string TargetOutput { get; init; }

    /// <summary>
    /// <c>true</c> when <see cref="OriginalDescriptor"/>'s content hash differs from
    /// <see cref="TargetDescriptor"/>'s — quick signal that the bodies are not byte-identical.
    /// </summary>
    public bool ContentHashChanged => !string.Equals(
        OriginalDescriptor.ContentHash,
        TargetDescriptor.ContentHash,
        StringComparison.Ordinal);
}
