using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.AI.Common.Prompts.Interfaces;
using Domain.AI.Prompts;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Prompts;

/// <summary>
/// File-backed <see cref="IPromptRegistry"/> that loads prompts from
/// <c>{RootPath}/{name}/v{Major}[.{Minor}].md</c>.
/// </summary>
/// <remarks>
/// <para>
/// File naming: <c>v1.md</c> ≡ <c>v1.0.md</c>; <c>v2.3.md</c> = major 2, minor 3.
/// Names are case-insensitive at lookup time; on disk the convention is kebab-case.
/// </para>
/// <para>
/// Loaded descriptors (body + SHA-256 content hash + parsed frontmatter) are cached
/// per (name, version) for the lifetime of the registry — templates are immutable
/// per version by convention.
/// </para>
/// <para>
/// Frontmatter is parsed as a simple <c>key: value</c> YAML-lite block (one pair per
/// line, no nested structures) so the registry has zero extra dependencies. Values
/// surface in <see cref="PromptDescriptor.Metadata"/> as opaque diagnostic data —
/// do NOT branch logic on metadata keys.
/// </para>
/// </remarks>
public sealed class FilePromptRegistry : IPromptRegistry
{
    private static readonly char[] FrontmatterKvSeparator = [':'];

    private readonly string _rootPath;
    private readonly ILogger<FilePromptRegistry> _logger;

    private readonly ConcurrentDictionary<(string Name, PromptVersion Version), PromptDescriptor> _byKey = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<PromptDescriptor>> _byName
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance pointing at <paramref name="rootPath"/>
    /// (typically the repo's top-level <c>prompts/</c> folder).
    /// </summary>
    public FilePromptRegistry(string rootPath, ILogger<FilePromptRegistry> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(logger);

        _rootPath = rootPath;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PromptDescriptor> GetLatestAsync(string name, CancellationToken cancellationToken)
    {
        var all = await ListAsync(name, cancellationToken).ConfigureAwait(false);
        if (all.Count == 0)
        {
            throw new KeyNotFoundException(
                $"No prompt versions found for '{name}' under '{_rootPath}'.");
        }
        return all[^1]; // ListAsync returns ascending — last is latest.
    }

    /// <inheritdoc />
    public async Task<PromptDescriptor> GetAsync(string name, PromptVersion version, CancellationToken cancellationToken)
    {
        var all = await ListAsync(name, cancellationToken).ConfigureAwait(false);
        foreach (var d in all)
        {
            if (d.Version == version) return d;
        }
        throw new KeyNotFoundException(
            $"Prompt '{name}' version {version} not found. Available: " +
            $"{string.Join(", ", all.Select(d => d.Version.ToString()))}");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PromptDescriptor>> ListAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        var cached = _byName.GetOrAdd(name, LoadName);
        return Task.FromResult(cached);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_rootPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var names = Directory.EnumerateDirectories(_rootPath)
            .Select(d => Path.GetFileName(d))
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private IReadOnlyList<PromptDescriptor> LoadName(string name)
    {
        var dir = Path.Combine(_rootPath, name);
        if (!Directory.Exists(dir))
        {
            _logger.LogDebug("Prompt directory '{Dir}' not found for name '{Name}'.", dir, name);
            return [];
        }

        var descriptors = new List<PromptDescriptor>();
        foreach (var file in Directory.EnumerateFiles(dir, "v*.md"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!PromptVersion.TryParse(fileName, out var version))
            {
                _logger.LogWarning(
                    "Skipping prompt file '{File}' under '{Name}' — filename '{FileName}' does not parse as a version.",
                    file, name, fileName);
                continue;
            }

            var descriptor = _byKey.GetOrAdd((name.ToLowerInvariant(), version), _ => LoadFile(name, version, file));
            descriptors.Add(descriptor);
        }

        descriptors.Sort((a, b) => a.Version.CompareTo(b.Version));
        return descriptors;
    }

    private static PromptDescriptor LoadFile(string name, PromptVersion version, string path)
    {
        var raw = File.ReadAllText(path);
        var (body, metadata) = SplitFrontmatter(raw);
        var hash = ComputeContentHash(body);

        return new PromptDescriptor
        {
            Name = name,
            Version = version,
            ContentHash = hash,
            Body = body,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Splits a leading YAML-lite frontmatter block (delimited by lines containing exactly
    /// <c>---</c>) from the body. Interior body lines like <c>---</c> are NOT treated as a
    /// closing fence — same line-anchored rule as <c>EmbeddedPromptTemplateLoader</c>.
    /// </summary>
    private static (string Body, IReadOnlyDictionary<string, string> Metadata) SplitFrontmatter(string raw)
    {
        var trimmed = raw.TrimStart();
        if (!trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            return (raw, new Dictionary<string, string>());
        }

        using var reader = new StringReader(trimmed);
        var opener = reader.ReadLine();
        if (opener is null || opener.TrimEnd() != "---")
        {
            return (raw, new Dictionary<string, string>());
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyBuilder = new StringBuilder(trimmed.Length);
        var sawClose = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!sawClose)
            {
                if (line.TrimEnd() == "---")
                {
                    sawClose = true;
                    continue;
                }
                // Parse "key: value" — single-line only, no nested structures.
                var parts = line.Split(FrontmatterKvSeparator, 2);
                if (parts.Length == 2)
                {
                    metadata[parts[0].Trim()] = parts[1].Trim();
                }
                continue;
            }
            bodyBuilder.Append(line).Append('\n');
        }

        return sawClose ? (bodyBuilder.ToString(), metadata) : (raw, new Dictionary<string, string>());
    }

    private static string ComputeContentHash(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
