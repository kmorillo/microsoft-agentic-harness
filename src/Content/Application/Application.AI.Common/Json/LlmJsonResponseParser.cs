using System.Text.Json;

namespace Application.AI.Common.Json;

/// <summary>
/// Resilient parser for LLM responses that are expected to contain a single JSON
/// object or array but are commonly wrapped in markdown fences, prose, or stray
/// characters.
/// </summary>
/// <remarks>
/// Handles:
/// <list type="bullet">
///   <item><description>Triple-backtick fenced blocks with or without a language tag (e.g. <c>```json</c>).</description></item>
///   <item><description>Leading or trailing prose around the JSON payload, even when the prose itself contains braces or brackets (uses a balanced-depth scanner that respects string literals).</description></item>
///   <item><description>Whitespace and newline noise.</description></item>
/// </list>
/// Returns <c>false</c> on any parse failure rather than throwing — callers translate
/// failures to retries or soft-fail verdicts.
/// </remarks>
public static class LlmJsonResponseParser
{
    /// <summary>
    /// Attempts to extract and deserialize the first balanced JSON OBJECT
    /// (<c>{</c>...<c>}</c>) from a raw LLM response. Tolerates prose with
    /// stray braces by tracking nesting depth and string-literal boundaries.
    /// </summary>
    /// <typeparam name="T">The shape the JSON object deserializes into.</typeparam>
    /// <param name="raw">The raw response text from the model.</param>
    /// <param name="options">Optional <see cref="JsonSerializerOptions"/> (e.g. case-insensitive property matching).</param>
    /// <param name="result">The deserialized result on success; <c>default</c> on failure.</param>
    /// <returns><c>true</c> when a JSON object was located and successfully deserialized; otherwise <c>false</c>.</returns>
    public static bool TryParseObject<T>(string? raw, JsonSerializerOptions? options, out T? result)
        where T : class
        => TryParse(raw, '{', '}', options, out result);

    /// <summary>
    /// Attempts to extract and deserialize the first balanced JSON ARRAY
    /// (<c>[</c>...<c>]</c>) from a raw LLM response. Tolerates prose with
    /// stray brackets by tracking nesting depth and string-literal boundaries.
    /// </summary>
    /// <typeparam name="T">The shape the JSON array deserializes into (typically <c>List&lt;TItem&gt;</c>).</typeparam>
    /// <param name="raw">The raw response text from the model.</param>
    /// <param name="options">Optional <see cref="JsonSerializerOptions"/>.</param>
    /// <param name="result">The deserialized result on success; <c>default</c> on failure.</param>
    /// <returns><c>true</c> when a JSON array was located and successfully deserialized; otherwise <c>false</c>.</returns>
    public static bool TryParseArray<T>(string? raw, JsonSerializerOptions? options, out T? result)
        where T : class
        => TryParse(raw, '[', ']', options, out result);

    private static bool TryParse<T>(
        string? raw,
        char open,
        char close,
        JsonSerializerOptions? options,
        out T? result)
        where T : class
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var stripped = StripFences(raw!);

        // Walk every candidate opener and attempt to deserialize each balanced span.
        // The first opener may bound a non-JSON span (e.g. prose '{final}' before
        // the real payload) — fall through to the next opener on parse failure.
        int from = 0;
        while (TryLocateBalancedSpan(stripped, open, close, from, out var start, out var length))
        {
            var slice = stripped.AsSpan(start, length);
            try
            {
                var deserialized = JsonSerializer.Deserialize<T>(slice, options);
                if (deserialized is not null)
                {
                    result = deserialized;
                    return true;
                }
            }
            catch (JsonException)
            {
                // Try the next candidate opener.
            }
            from = start + 1;
        }

        return false;
    }

    /// <summary>
    /// Locates the next balanced <paramref name="open"/>...<paramref name="close"/> span
    /// starting at or after <paramref name="searchFrom"/>, respecting JSON string-literal
    /// boundaries so braces inside <c>"..."</c> do not affect depth.
    /// </summary>
    /// <remarks>
    /// Lexer-aware scan rather than a naive <c>IndexOf</c>/<c>LastIndexOf</c>, so prose
    /// like <c>Here is my {final} verdict: {"score": 0.8}</c> can yield multiple
    /// candidates and the caller picks the first that deserializes successfully.
    /// </remarks>
    private static bool TryLocateBalancedSpan(
        string text,
        char open,
        char close,
        int searchFrom,
        out int start,
        out int length)
    {
        start = 0;
        length = 0;

        for (int i = searchFrom; i < text.Length; i++)
        {
            if (text[i] != open) continue;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int j = i; j < text.Length; j++)
            {
                var c = text[j];

                if (escaped) { escaped = false; continue; }
                if (inString)
                {
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }

                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        start = i;
                        length = j - i + 1;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Strips a single leading triple-backtick fence (with optional language tag)
    /// and a trailing triple-backtick fence from <paramref name="raw"/>. Returns
    /// the input unchanged if no fence pair is present at the boundaries.
    /// </summary>
    /// <param name="raw">The raw response text.</param>
    /// <returns>The fence-stripped text, trimmed.</returns>
    public static string StripFences(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var s = raw.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;

        var firstNewline = s.IndexOf('\n');
        if (firstNewline >= 0) s = s[(firstNewline + 1)..];
        if (s.EndsWith("```", StringComparison.Ordinal)) s = s[..^3];
        return s.Trim();
    }
}
