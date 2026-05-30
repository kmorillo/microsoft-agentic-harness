using Domain.AI.Prompts;

namespace Application.AI.Common.Prompts.Models;

/// <summary>
/// A single occurrence of a prompt being resolved + used at runtime. Captured by
/// <see cref="Interfaces.IPromptUsageRecorder"/> and persisted so trace-replay can
/// answer "which prompt version did this trace use, and what was its hash?"
/// </summary>
public sealed record PromptUsageRecord
{
    /// <summary>The descriptor identifying which (name, version) was used.</summary>
    public required PromptDescriptor Descriptor { get; init; }

    /// <summary>OTel/W3C trace ID this usage was recorded on. Hex, no dashes.</summary>
    public string? TraceId { get; init; }

    /// <summary>OTel/W3C span ID this usage was recorded on. Hex, no dashes.</summary>
    public string? SpanId { get; init; }

    /// <summary>UTC timestamp the usage was recorded.</summary>
    public required DateTimeOffset RecordedAtUtc { get; init; }
}
