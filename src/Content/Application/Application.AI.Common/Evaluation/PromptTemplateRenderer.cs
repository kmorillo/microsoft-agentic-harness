using System.Net;
using System.Text;

namespace Application.AI.Common.Evaluation;

/// <summary>
/// Renders prompt templates by substituting <c>{{variable_name}}</c> placeholders
/// with HTML-escaped values from a dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Variable values are passed through <see cref="WebUtility.HtmlEncode(string?)"/>
/// before substitution as a defense-in-depth measure against prompt-injection via
/// angle-bracketed delimiter mimicry — the same mitigation <c>LlmJudgeMetric</c>
/// applies to the rubric judge.
/// </para>
/// <para>
/// Substitution is single-pass and literal — no recursive expansion, no conditionals,
/// no loops. Sub-phase 5.3 may replace this with Scriban variable-only mode.
/// Unknown placeholders are left in the rendered text and logged so consumers
/// can spot template/data drift.
/// </para>
/// </remarks>
public static class PromptTemplateRenderer
{
    /// <summary>
    /// Substitutes <c>{{key}}</c> placeholders in <paramref name="template"/> with
    /// HTML-escaped values from <paramref name="variables"/>. Missing keys are left
    /// in place and reported in <paramref name="unresolved"/>.
    /// </summary>
    /// <param name="template">The raw template text.</param>
    /// <param name="variables">Key/value pairs to substitute. Keys are case-sensitive.</param>
    /// <param name="unresolved">Placeholder names that had no matching value.</param>
    /// <returns>The rendered text.</returns>
    public static string Render(
        string template,
        IReadOnlyDictionary<string, string?> variables,
        out IReadOnlyList<string> unresolved)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        var missing = new List<string>();
        var sb = new StringBuilder(template.Length);

        int i = 0;
        while (i < template.Length)
        {
            // Look for the next "{{" opener.
            var open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            sb.Append(template, i, open - i);

            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                // No closing braces — copy the rest verbatim and stop.
                sb.Append(template, open, template.Length - open);
                break;
            }

            var name = template.Substring(open + 2, close - (open + 2)).Trim();
            if (variables.TryGetValue(name, out var value))
            {
                sb.Append(WebUtility.HtmlEncode(value ?? string.Empty));
            }
            else
            {
                // Unknown placeholder — preserve verbatim so it's visible in the rendered prompt.
                missing.Add(name);
                sb.Append(template, open, close + 2 - open);
            }

            i = close + 2;
        }

        unresolved = missing;
        return sb.ToString();
    }
}
