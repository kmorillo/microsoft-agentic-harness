using Domain.AI.Prompts;

namespace Application.AI.Common.Prompts.Interfaces;

/// <summary>
/// Renders a <see cref="PromptDescriptor"/> with caller-supplied variables, producing
/// a <see cref="RenderedPrompt"/> ready for an LLM call. Locked to variable
/// interpolation only — no loops, no conditionals — per the Sub-phase 5.3 plan.
/// </summary>
/// <remarks>
/// The plan locks Scriban as the rendering engine. The interface stays engine-agnostic
/// so a future swap to Liquid or a custom parser doesn't break callers. Implementations
/// should HTML-escape user-supplied values where applicable; details depend on the
/// destination — judge prompts already wrap in a nonce envelope, while agent system
/// prompts may need different handling.
/// </remarks>
public interface IPromptRenderer
{
    /// <summary>
    /// Substitutes <paramref name="variables"/> into <paramref name="descriptor"/>'s
    /// body and returns the rendered output plus any unresolved placeholders.
    /// </summary>
    /// <param name="descriptor">The prompt descriptor to render.</param>
    /// <param name="variables">Variable values keyed by placeholder name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered prompt + diagnostic info.</returns>
    Task<RenderedPrompt> RenderAsync(
        PromptDescriptor descriptor,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken);
}
