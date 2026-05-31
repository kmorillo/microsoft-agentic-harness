using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Application.AI.Common.Prompts.Exceptions;
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
/// <para>
/// <b>Exception contract.</b> Honors the <see cref="IPromptRegistry"/> surface: every
/// transient file-system exception (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>,
/// <see cref="SecurityException"/>, etc.) is wrapped in
/// <see cref="PromptRegistryUnavailableException"/>; missing prompts surface as
/// <see cref="KeyNotFoundException"/>. Backends-specific exceptions do not escape.
/// </para>
/// <para>
/// <b>Negative cache.</b> When <see cref="ListAsync"/> returns empty (name unknown to
/// the registry) the empty result is cached for <see cref="NegativeCacheTtl"/> so a
/// burst of "prompt-typo" requests does not amplify disk scans. After the TTL elapses,
/// the next call re-probes — supporting the "prompt directory added at runtime" case
/// without unbounded disk pressure on a permanent miss.
/// </para>
/// </remarks>
public sealed class FilePromptRegistry : IPromptRegistry
{
    /// <summary>How long an empty (negative) result stays cached before re-probing the disk.</summary>
    public static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromSeconds(30);

    private static readonly char[] FrontmatterKvSeparator = [':'];

    private readonly string _rootPath;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FilePromptRegistry> _logger;

    private readonly ConcurrentDictionary<(string Name, PromptVersion Version), PromptDescriptor> _byKey = new();

    // Lazy<> wraps the loader so ConcurrentDictionary.GetOrAdd's non-atomic value
    // factory can't trigger N parallel disk scans on a cold key — only the first
    // thread to call .Value runs LoadName. The Lazy itself never throws because
    // LoadName converts every transient backend exception into a sentinel
    // CacheEntry.Faulted — so a one-time IO hiccup doesn't pin a faulted Lazy
    // in the dictionary forever.
    private readonly ConcurrentDictionary<string, Lazy<CacheEntry>> _byName
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance pointing at <paramref name="rootPath"/>
    /// (typically the repo's top-level <c>prompts/</c> folder).
    /// </summary>
    public FilePromptRegistry(string rootPath, ILogger<FilePromptRegistry> logger)
        : this(rootPath, TimeProvider.System, logger) { }

    /// <summary>
    /// Initializes a new instance with an explicit <see cref="TimeProvider"/> (test seam
    /// for the negative-cache TTL).
    /// </summary>
    public FilePromptRegistry(string rootPath, TimeProvider timeProvider, ILogger<FilePromptRegistry> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _rootPath = rootPath;
        _timeProvider = timeProvider;
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

        // Pre-evict an expired negative-cached entry BEFORE GetOrAdd so the upcoming
        // call re-probes the disk. KVP overload so we only evict the Lazy we observed —
        // a concurrent successful re-probe remains intact.
        if (_byName.TryGetValue(name, out var existing)
            && existing.IsValueCreated
            && existing.Value.Faulted is null
            && existing.Value.Descriptors.Count == 0
            && _timeProvider.GetUtcNow() - existing.Value.LoadedAt >= NegativeCacheTtl)
        {
            _byName.TryRemove(new KeyValuePair<string, Lazy<CacheEntry>>(name, existing));
        }

        var lazy = _byName.GetOrAdd(name, n => new Lazy<CacheEntry>(
            () => LoadEntry(n),
            LazyThreadSafetyMode.ExecutionAndPublication));

        var entry = lazy.Value;

        if (entry.Faulted is { } unavailable)
        {
            // Don't keep faulted entries: the next caller should retry against the
            // backend rather than replay a stale failure.
            _byName.TryRemove(new KeyValuePair<string, Lazy<CacheEntry>>(name, lazy));
            throw unavailable;
        }

        return Task.FromResult(entry.Descriptors);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
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
        catch (Exception ex) when (IsTransientBackendException(ex))
        {
            throw new PromptRegistryUnavailableException(
                "(listing all names)",
                $"Failed to enumerate prompt directories under '{_rootPath}'.",
                ex);
        }
    }

    /// <summary>
    /// Loads a single name's descriptors and translates any backend exception into a
    /// <see cref="CacheEntry"/> so the Lazy never throws.
    /// </summary>
    private CacheEntry LoadEntry(string name)
    {
        var loadedAt = _timeProvider.GetUtcNow();
        try
        {
            return new CacheEntry(LoadName(name), loadedAt, Faulted: null);
        }
        catch (Exception ex) when (IsTransientBackendException(ex))
        {
            _logger.LogWarning(ex, "Transient failure loading prompts for '{Name}' under '{Root}'.", name, _rootPath);
            return new CacheEntry(
                Descriptors: [],
                loadedAt,
                Faulted: new PromptRegistryUnavailableException(
                    name,
                    $"Failed to load prompt '{name}' from '{_rootPath}'.",
                    ex));
        }
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
    /// closing fence.
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

    private static bool IsTransientBackendException(Exception ex)
        => ex is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException
            or ArgumentException;

    /// <summary>
    /// Cache entry — either a successful list of descriptors (<see cref="Faulted"/> = null)
    /// or a soft-failed load whose original exception is preserved for the next caller to
    /// observe and re-wrap.
    /// </summary>
    private sealed record CacheEntry(
        IReadOnlyList<PromptDescriptor> Descriptors,
        DateTimeOffset LoadedAt,
        PromptRegistryUnavailableException? Faulted);
}
