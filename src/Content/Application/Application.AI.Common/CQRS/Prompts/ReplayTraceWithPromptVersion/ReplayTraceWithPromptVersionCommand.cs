using Domain.AI.Prompts;
using Domain.Common;
using Domain.Common.Config.AI;
using MediatR;

namespace Application.AI.Common.CQRS.Prompts.ReplayTraceWithPromptVersion;

/// <summary>
/// Replays a historical LLM call against a different prompt version so a prompt-author
/// can A/B-compare what the assistant would have produced if the new version had been
/// in place at the time of the original trace.
/// </summary>
/// <remarks>
/// <para>
/// Workflow:
/// <list type="number">
///   <item><description>Locate the original prompt-usage row via (<see cref="TraceId"/>, <see cref="PromptName"/>) — proves the prompt was actually used at the trace time and recovers the original version.</description></item>
///   <item><description>Resolve both versions from the registry: the original (for diff diagnostics) and the target (for the replay LLM call).</description></item>
///   <item><description>Render both descriptors with the caller-supplied <see cref="Variables"/>. Variables are NOT stored in the usage row — the caller is responsible for providing the original input.</description></item>
///   <item><description>Invoke the LLM with the target rendered body at <b>temperature = 0</b>. Temperature is forced regardless of the original trace's setting so any observed delta is attributable to the prompt change, not sampling noise.</description></item>
///   <item><description>Return <see cref="PromptReplayResult"/> with both renders, the target output, and a content-hash-changed flag.</description></item>
/// </list>
/// </para>
/// <para>
/// Failure modes flow through <see cref="Result{T}"/>:
/// <list type="bullet">
///   <item><description><see cref="Result{T}.NotFound"/> when no usage row matches (TraceId, PromptName) or the target version doesn't exist in the registry.</description></item>
///   <item><description><see cref="Result{T}.Fail(string[])"/> when the original version has since been removed from the registry (cannot recover diff baseline).</description></item>
///   <item><description><see cref="Result{T}.ValidationFailure"/> from the validator for missing required fields.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed record ReplayTraceWithPromptVersionCommand : IRequest<Result<PromptReplayResult>>
{
    /// <summary>OTel/W3C trace id identifying the historical case to replay (hex, no dashes).</summary>
    public required string TraceId { get; init; }

    /// <summary>Registry name of the prompt to swap (e.g. <c>"faithfulness-judge"</c>).</summary>
    public required string PromptName { get; init; }

    /// <summary>Target prompt version to replay against.</summary>
    public required PromptVersion TargetVersion { get; init; }

    /// <summary>
    /// Original variables used to render the prompt at the historical trace. The
    /// usage store does not persist input variables, so the caller supplies them
    /// (typically from their eval report or trace exporter).
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }

    /// <summary>LLM provider type to use for the replay (Azure OpenAI / OpenAI / etc.).</summary>
    public required AIAgentFrameworkClientType ChatClientType { get; init; }

    /// <summary>Deployment / model name for the replay LLM.</summary>
    public required string Deployment { get; init; }

    /// <summary>
    /// Optional explicit max-tokens cap for the replay response. Defaults to <c>4096</c>
    /// when not supplied.
    /// </summary>
    public int? MaxOutputTokens { get; init; }
}
