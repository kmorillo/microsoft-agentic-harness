namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Loads a named prompt template as raw text. The default implementation reads from
/// embedded markdown resources shipped with the eval framework
/// (<c>Application.AI.Common/Evaluation/Prompts/*.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// Templates use double-brace placeholders (<c>{{variable_name}}</c>) substituted by
/// <c>PromptTemplateRenderer.Render</c>. Sub-phase 5.3 will introduce a richer
/// Scriban-backed registry; this loader is the simpler interim.
/// </para>
/// <para>
/// Implementations should cache parsed templates — they're effectively immutable
/// at runtime.
/// </para>
/// </remarks>
public interface IPromptTemplateLoader
{
    /// <summary>
    /// Loads a template by its logical name (e.g. <c>"faithfulness"</c>, <c>"context-precision"</c>).
    /// </summary>
    /// <param name="templateName">The template identifier without file extension.</param>
    /// <returns>The raw template body, with placeholders still un-substituted.</returns>
    /// <exception cref="FileNotFoundException">When no template is found for the supplied name.</exception>
    string Load(string templateName);
}
