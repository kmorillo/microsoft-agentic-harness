using System.Globalization;

namespace Domain.AI.Prompts;

/// <summary>
/// Two-segment prompt version (Major.Minor) — no patch level.
/// </summary>
/// <remarks>
/// <para>
/// Patch is omitted intentionally: prompt templates are the LLM's input contract,
/// not a library API, so the distinction between minor and patch loses meaning.
/// A two-segment scheme keeps the version surface narrow:
/// <list type="bullet">
///   <item><description><b>Major</b> bump when meaning changes (different output shape, different scoring rubric, different role).</description></item>
///   <item><description><b>Minor</b> bump for any other edit (wording, examples, formatting) that callers may want to A/B but is functionally equivalent.</description></item>
/// </list>
/// </para>
/// <para>
/// Versions are ordered by Major then Minor. Parsed from filename suffixes like
/// <c>v1.md</c>, <c>v1.2.md</c>, <c>v2.0.md</c>.
/// </para>
/// </remarks>
public readonly record struct PromptVersion(int Major, int Minor) : IComparable<PromptVersion>
{
    /// <summary>Minimal valid version (<c>v1.0</c>).</summary>
    public static PromptVersion V1 { get; } = new(1, 0);

    /// <summary>Renders the version as <c>v{Major}.{Minor}</c>.</summary>
    public override string ToString() => $"v{Major}.{Minor}";

    /// <summary>
    /// Parses a version string produced by <see cref="ToString"/>, or a shorthand
    /// without minor (<c>v1</c> ≡ <c>v1.0</c>).
    /// </summary>
    /// <param name="text">The version text. Accepts <c>v1</c>, <c>v1.2</c>, <c>1</c>, <c>1.2</c>.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="FormatException">When the text does not match the expected shape.</exception>
    public static PromptVersion Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var trimmed = text.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[1..];
        }

        var dot = trimmed.IndexOf('.');
        if (dot < 0)
        {
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
                ? new PromptVersion(major, 0)
                : throw new FormatException($"Invalid prompt version '{text}'. Expected 'v{{major}}' or 'v{{major}}.{{minor}}'.");
        }

        var majorPart = trimmed[..dot];
        var minorPart = trimmed[(dot + 1)..];
        if (!int.TryParse(majorPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(minorPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            throw new FormatException($"Invalid prompt version '{text}'. Expected integer major and minor segments.");
        }

        return new PromptVersion(m, n);
    }

    /// <summary>Tries to parse without throwing.</summary>
    public static bool TryParse(string? text, out PromptVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { version = Parse(text); return true; }
        catch (FormatException) { return false; }
    }

    /// <inheritdoc />
    public int CompareTo(PromptVersion other)
    {
        var byMajor = Major.CompareTo(other.Major);
        return byMajor != 0 ? byMajor : Minor.CompareTo(other.Minor);
    }

    /// <summary>Less-than comparison by Major then Minor.</summary>
    public static bool operator <(PromptVersion left, PromptVersion right) => left.CompareTo(right) < 0;
    /// <summary>Greater-than comparison by Major then Minor.</summary>
    public static bool operator >(PromptVersion left, PromptVersion right) => left.CompareTo(right) > 0;
    /// <summary>Less-than-or-equal comparison by Major then Minor.</summary>
    public static bool operator <=(PromptVersion left, PromptVersion right) => left.CompareTo(right) <= 0;
    /// <summary>Greater-than-or-equal comparison by Major then Minor.</summary>
    public static bool operator >=(PromptVersion left, PromptVersion right) => left.CompareTo(right) >= 0;
}
