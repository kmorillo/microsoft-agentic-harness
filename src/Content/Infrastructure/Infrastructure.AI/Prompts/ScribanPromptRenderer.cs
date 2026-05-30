using System.Net;
using Application.AI.Common.Prompts.Interfaces;
using Domain.AI.Prompts;
using Microsoft.Extensions.Logging;
using Scriban;
using Scriban.Runtime;

namespace Infrastructure.AI.Prompts;

/// <summary>
/// Renders <see cref="PromptDescriptor"/> bodies via the Scriban template engine,
/// locked to variable-interpolation only per the Sub-phase 5.3 plan: no loops,
/// no conditionals, no function calls.
/// </summary>
/// <remarks>
/// <para>
/// String values are HTML-encoded on substitution as defense-in-depth (same
/// posture as <c>PromptTemplateRenderer</c> used by the 5.2 eval pack). Non-string
/// values are passed through Scriban's default ToString; callers that need richer
/// rendering should stringify ahead of time.
/// </para>
/// <para>
/// Templates use Scriban's default <c>{{ var }}</c> syntax. Parse errors throw at
/// render time; callers translate to soft-fails as appropriate.
/// </para>
/// </remarks>
public sealed class ScribanPromptRenderer : IPromptRenderer
{
    private readonly ILogger<ScribanPromptRenderer> _logger;

    /// <summary>Initializes a new instance.</summary>
    public ScribanPromptRenderer(ILogger<ScribanPromptRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RenderedPrompt> RenderAsync(
        PromptDescriptor descriptor,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(variables);

        var template = Template.Parse(descriptor.Body);
        if (template.HasErrors)
        {
            var firstError = template.Messages.FirstOrDefault();
            throw new InvalidOperationException(
                $"Scriban parse error in prompt '{descriptor.Identifier}': {firstError?.Message ?? "(unknown)"}");
        }

        // Pre-encode string values; pass non-strings through so Scriban's default
        // ToString handles them. Variable lookup is case-insensitive at the Scriban
        // layer to match common author expectations.
        var script = new ScriptObject();
        foreach (var (key, value) in variables)
        {
            script[key] = value is string s ? WebUtility.HtmlEncode(s) : value;
        }

        // Track unresolved placeholders by walking Scriban's AST for variable refs
        // not present in `script`. This is best-effort — the rendered body still ships
        // even if some placeholders weren't substituted (Scriban renders them as empty).
        var unresolved = ScanForUnresolvedVariables(template, script);

        var context = new TemplateContext();
        context.PushGlobal(script);

        try
        {
            var body = await template.RenderAsync(context).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (unresolved.Count > 0)
            {
                _logger.LogWarning(
                    "Unresolved variables in prompt '{Prompt}': {Vars}",
                    descriptor.Identifier, string.Join(", ", unresolved));
            }

            return new RenderedPrompt
            {
                Source = descriptor,
                Body = body,
                Unresolved = unresolved
            };
        }
        finally
        {
            context.PopGlobal();
        }
    }

    private static IReadOnlyList<string> ScanForUnresolvedVariables(Template template, ScriptObject script)
    {
        if (template.Page is null) return [];

        var missing = new List<string>();
        WalkVariableReferences(template.Page, name =>
        {
            if (!script.ContainsKey(name))
            {
                missing.Add(name);
            }
        });
        return missing;
    }

    private static void WalkVariableReferences(Scriban.Syntax.ScriptNode node, Action<string> onVariableName)
    {
        if (node is Scriban.Syntax.ScriptVariableGlobal v)
        {
            onVariableName(v.Name);
            return;
        }

        // Walk children for any node type that has them.
        for (int i = 0; i < node.ChildrenCount; i++)
        {
            var child = node.GetChildren(i);
            if (child is not null) WalkVariableReferences(child, onVariableName);
        }
    }
}
