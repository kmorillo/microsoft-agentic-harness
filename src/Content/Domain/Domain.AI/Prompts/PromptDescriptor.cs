namespace Domain.AI.Prompts;

/// <summary>
/// Identifies a single prompt template in the registry: its logical name, its
/// version, and a content-addressable hash so trace records can prove exactly
/// which body text the LLM saw.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Name"/> is the registry-wide identifier (e.g. <c>"faithfulness-judge"</c>,
/// <c>"agent-system"</c>). Naming convention is kebab-case so file paths
/// <c>prompts/{name}/v{version}.md</c> are filesystem-friendly across platforms.
/// </para>
/// <para>
/// <see cref="ContentHash"/> is a SHA-256 of the on-disk body bytes (after frontmatter
/// strip, before substitution). Surface goal: two trace replays that resolve the same
/// (name, version) on different machines must match by hash, or the trace replay is
/// invalid. The hash is computed by the registry at load time, not the caller.
/// </para>
/// </remarks>
public sealed record PromptDescriptor
{
    /// <summary>Registry name (kebab-case).</summary>
    public required string Name { get; init; }

    /// <summary>Version of this descriptor in the registry.</summary>
    public required PromptVersion Version { get; init; }

    /// <summary>Hex-encoded SHA-256 of the loaded prompt body. Lowercase.</summary>
    public required string ContentHash { get; init; }

    /// <summary>The raw prompt body, post-frontmatter-strip, pre-substitution.</summary>
    public required string Body { get; init; }

    /// <summary>
    /// Optional descriptor metadata parsed from frontmatter (e.g. <c>description</c>,
    /// <c>inputs</c>, <c>owner</c>). Loader-specific; consumers treat as opaque
    /// diagnostic data — do NOT branch logic on these keys.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Pretty identifier suitable for OTel tags / logs: <c>name@v{Major}.{Minor}</c>.
    /// </summary>
    public string Identifier => $"{Name}@{Version}";
}
