using System.Collections.Concurrent;
using System.Reflection;
using Application.AI.Common.Evaluation.Interfaces;

namespace Infrastructure.AI.Evaluation.Prompts;

/// <summary>
/// Loads judge prompt templates from manifest resources embedded in
/// <c>Application.AI.Common</c> (<c>Evaluation/Prompts/*.md</c>).
/// </summary>
/// <remarks>
/// Strips the optional YAML frontmatter block (delimited by <c>---</c> on its own
/// line at both ends) so only the prompt body is rendered. Loaded templates are
/// cached for the lifetime of the loader instance.
/// </remarks>
public sealed class EmbeddedPromptTemplateLoader : IPromptTemplateLoader
{
    private readonly Assembly _assembly;
    private readonly string _resourceNamespace;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance loading from the default <c>Application.AI.Common</c>
    /// assembly and namespace.
    /// </summary>
    public EmbeddedPromptTemplateLoader()
        : this(
            typeof(Application.AI.Common.Evaluation.PromptTemplateRenderer).Assembly,
            "Application.AI.Common.Evaluation.Prompts")
    {
    }

    /// <summary>
    /// Initializes a new instance loading from a custom assembly and namespace prefix.
    /// </summary>
    /// <param name="assembly">Assembly containing the embedded template resources.</param>
    /// <param name="resourceNamespace">Manifest-resource namespace prefix for the templates.</param>
    public EmbeddedPromptTemplateLoader(Assembly assembly, string resourceNamespace)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceNamespace);

        _assembly = assembly;
        _resourceNamespace = resourceNamespace;
    }

    /// <inheritdoc />
    public string Load(string templateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);

        return _cache.GetOrAdd(templateName, name =>
        {
            // Resource files are normalized by MSBuild to use dots; the file
            // "context-precision.judge.md" lands as
            // "Application.AI.Common.Evaluation.Prompts.context-precision.judge.md".
            var resourceName = $"{_resourceNamespace}.{name}.judge.md";

            var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                var available = string.Join(
                    ", ",
                    _assembly.GetManifestResourceNames()
                        .Where(r => r.Contains("Prompts", StringComparison.OrdinalIgnoreCase)));
                throw new FileNotFoundException(
                    $"Embedded prompt template '{name}' not found at resource '{resourceName}'. Available: {available}");
            }

            using (stream)
            using (var reader = new StreamReader(stream))
            {
                var raw = reader.ReadToEnd();
                return StripFrontmatter(raw);
            }
        });
    }

    /// <summary>
    /// Removes a leading YAML frontmatter block delimited by a line containing EXACTLY
    /// <c>---</c> at the open and at the close. Returns the input unchanged when no
    /// frontmatter is present or when no matching close fence is found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Line-anchored: an interior body line like <c>---</c> (markdown horizontal rule),
    /// <c>------</c>, or <c>---END</c> is NOT treated as a closing fence, so template
    /// bodies are never silently truncated by body content that happens to begin with
    /// three dashes. The closing fence must be exactly <c>---</c> (optionally with
    /// trailing whitespace / CR), nothing more.
    /// </para>
    /// <para>
    /// Line endings are normalized to LF. Embedded templates authored on Windows ship
    /// as CRLF but emerge from this loader as LF-only — intentional: the rendered prompt
    /// the judge receives is functionally identical, and uniform line endings make
    /// snapshot / golden-master tests stable across platforms. Don't write tests that
    /// assert CRLF in the body.
    /// </para>
    /// </remarks>
    private static string StripFrontmatter(string raw)
    {
        var trimmed = raw.TrimStart();
        if (!trimmed.StartsWith("---", StringComparison.Ordinal)) return raw;

        using var reader = new StringReader(trimmed);
        var openerLine = reader.ReadLine();
        if (openerLine is null || openerLine.TrimEnd() != "---")
        {
            // First line wasn't a pure "---" (e.g. "---START") — treat as body.
            return raw;
        }

        var bodyBuilder = new System.Text.StringBuilder(trimmed.Length);
        bool sawClose = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!sawClose)
            {
                if (line.TrimEnd() == "---")
                {
                    sawClose = true;
                }
                // Else: frontmatter content line — discard.
                continue;
            }
            bodyBuilder.Append(line).Append('\n');
        }

        // No matching closing fence — return raw unchanged rather than silently drop content.
        return sawClose ? bodyBuilder.ToString() : raw;
    }
}
