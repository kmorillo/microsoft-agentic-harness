using Domain.AI.Prompts;

namespace Application.AI.Common.Prompts.Models;

/// <summary>
/// A single occurrence of a prompt being resolved + used at runtime. Captured by
/// <see cref="Interfaces.IPromptUsageRecorder"/> and persisted so trace-replay can
/// answer "which prompt version did this case use, and what was its hash?"
/// </summary>
public sealed record PromptUsageRecord
{
    /// <summary>The descriptor identifying which (name, version) was used.</summary>
    public required PromptDescriptor Descriptor { get; init; }

    /// <summary>
    /// Identifier of the case / unit-of-work that consumed the prompt. Mirrors
    /// <c>PromptUsageContext.CaseId</c> at the time of the call.
    /// </summary>
    public string? CaseId { get; init; }

    /// <summary>
    /// Consuming surface identifier (e.g. metric key, command name). Mirrors
    /// <c>PromptUsageContext.MetricKey</c> at the time of the call.
    /// </summary>
    public string? MetricKey { get; init; }

    /// <summary>OTel/W3C trace ID this usage was recorded on. Hex, no dashes.</summary>
    public string? TraceId { get; init; }

    /// <summary>OTel/W3C span ID this usage was recorded on. Hex, no dashes.</summary>
    public string? SpanId { get; init; }

    /// <summary>UTC timestamp the usage was recorded.</summary>
    public required DateTimeOffset RecordedAtUtc { get; init; }
}
