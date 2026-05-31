namespace Domain.AI.Prompts;

/// <summary>
/// Per-invocation context passed to <c>IPromptUsageRecorder.RecordAsync</c> alongside
/// the resolved <see cref="PromptDescriptor"/>. Lets recorders attribute the prompt
/// to the specific case / command / span that consumed it without depending on
/// ambient <c>Activity.Current</c>.
/// </summary>
/// <remarks>
/// All fields are optional. Recorders should tolerate every combination of supplied /
/// missing values. <see cref="Empty"/> is the canonical "no caller context" value —
/// useful for code paths that have only the descriptor.
/// </remarks>
public sealed record PromptUsageContext
{
    /// <summary>
    /// Identifier of the case / unit-of-work that triggered the prompt resolution
    /// (e.g. <c>EvalCase.Id</c> for offline eval, or a MediatR command correlation id).
    /// </summary>
    public string? CaseId { get; init; }

    /// <summary>
    /// Identifier of the consuming surface (e.g. metric key like <c>"faithfulness"</c>,
    /// command name, or pipeline stage). Lets observability split prompt usage by
    /// consumer without inspecting the call stack.
    /// </summary>
    public string? MetricKey { get; init; }

    /// <summary>
    /// Free-form tags propagated to recorder back-ends. Implementations decide whether
    /// to materialize these as OTel tags, SQL columns, or ignore them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>Canonical empty context for callers that have only the descriptor.</summary>
    public static PromptUsageContext Empty { get; } = new();
}
