namespace Domain.AI.Prompts;

/// <summary>
/// A prompt that has been resolved AND had its variables substituted, ready to send
/// to a chat model. Carries a back-reference to the source descriptor so trace
/// observers can record exactly which (name, version) produced this body.
/// </summary>
public sealed record RenderedPrompt
{
    /// <summary>The source descriptor (name + version + content hash).</summary>
    public required PromptDescriptor Source { get; init; }

    /// <summary>The fully-rendered prompt body, ready for an LLM call.</summary>
    public required string Body { get; init; }

    /// <summary>
    /// Names of any <c>{{placeholders}}</c> that had no matching variable supplied
    /// and were left in place in <see cref="Body"/>. Empty when rendering was complete.
    /// </summary>
    public IReadOnlyList<string> Unresolved { get; init; } = [];
}
