namespace Domain.AI.Observability.Models;

/// <summary>
/// Represents a single tool invocation within a session.
/// </summary>
public sealed record ToolExecutionRecord
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public Guid? MessageId { get; init; }
    public required string ToolName { get; init; }
    public string? ToolSource { get; init; }
    public int? DurationMs { get; init; }
    public required string Status { get; init; }
    public string? ErrorType { get; init; }
    public int? ResultSize { get; init; }

    /// <summary>
    /// LLM-supplied call id from <c>FunctionCallContent.CallId</c>, used to pair
    /// the function-call request with its corresponding function-result message.
    /// May be null for legacy rows or providers that don't surface a call id.
    /// </summary>
    public string? CallId { get; init; }

    /// <summary>
    /// JSON-serialized arguments the LLM passed to the tool, captured from
    /// <c>FunctionCallContent.Arguments</c>. Untrusted content — render escaped
    /// in the UI.
    /// </summary>
    public string? Args { get; init; }

    /// <summary>
    /// Tool return value as returned to the LLM, captured from
    /// <c>FunctionResultContent.Result</c>. Untrusted content — render escaped.
    /// Stored verbatim (no truncation); callers reading large rows should page or
    /// stream if needed.
    /// </summary>
    public string? Stdout { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
