using System.Text.Json;
using System.Text.Json.Nodes;

namespace Infrastructure.AI.Caching;

/// <summary>
/// Pure transform that stamps an Anthropic prompt-cache breakpoint
/// (<c>cache_control: {"type": "ephemeral"}</c>) onto the system message of an
/// OpenAI-format chat-completions request body.
/// </summary>
/// <remarks>
/// <para>
/// Anthropic prompt caching is opt-in: a request must mark which prefix to cache. When the harness
/// talks to Claude through an OpenAI-compatible gateway (e.g. OpenRouter), the breakpoint is a
/// <c>cache_control</c> field on a content block inside the <c>messages</c> array. This transform
/// places a single breakpoint on the <em>last</em> system message — the large, stable prefix
/// (system prompt, and any tool preamble folded into the system block) that repeats every turn.
/// Everything up to and including that block becomes cache-eligible.
/// </para>
/// <para>
/// The transform is defensive by construction: any body it does not recognise (malformed JSON, no
/// <c>messages</c> array, no system message, an unexpected content shape) is returned byte-for-byte
/// unchanged, so it can sit on the live request path without risk of corrupting a request. It is
/// also idempotent — a body that already carries a breakpoint is left as-is.
/// </para>
/// <para>
/// Caching only takes effect when the cached prefix exceeds the provider's minimum size (~1024
/// tokens for Sonnet/Opus) and the prefix is byte-identical across turns; below that, the provider
/// silently ignores the breakpoint, so stamping it unconditionally is safe.
/// </para>
/// </remarks>
public static class PromptCacheInjector
{
    private const string MessagesProperty = "messages";
    private const string RoleProperty = "role";
    private const string ContentProperty = "content";
    private const string CacheControlProperty = "cache_control";
    private const string SystemRole = "system";

    /// <summary>
    /// Returns <paramref name="requestJson"/> with a <c>cache_control: ephemeral</c> breakpoint on
    /// the last system message, or the original string unchanged when no safe injection applies.
    /// </summary>
    /// <param name="requestJson">The serialized OpenAI chat-completions request body.</param>
    /// <returns>The rewritten body, or <paramref name="requestJson"/> unchanged.</returns>
    public static string InjectSystemCacheControl(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
            return requestJson;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(requestJson);
        }
        catch (JsonException)
        {
            return requestJson;
        }

        if (root is not JsonObject body || body[MessagesProperty] is not JsonArray messages)
            return requestJson;

        var systemMessage = FindLastSystemMessage(messages);
        if (systemMessage is null)
            return requestJson;

        return TryMark(systemMessage) ? body.ToJsonString() : requestJson;
    }

    private static JsonObject? FindLastSystemMessage(JsonArray messages)
    {
        JsonObject? found = null;
        foreach (var node in messages)
        {
            if (node is JsonObject message
                && message[RoleProperty]?.GetValue<string>() == SystemRole)
            {
                found = message;
            }
        }

        return found;
    }

    /// <summary>Marks the system message in place. Returns false when the content shape is unsupported.</summary>
    private static bool TryMark(JsonObject systemMessage)
    {
        var content = systemMessage[ContentProperty];

        // Plain string content → convert to a single cached text block.
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            systemMessage[ContentProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                    [CacheControlProperty] = Ephemeral()
                }
            };
            return true;
        }

        // Array content → mark the last block (idempotent).
        if (content is JsonArray parts && parts.Count > 0 && parts[^1] is JsonObject lastBlock)
        {
            if (lastBlock[CacheControlProperty] is null)
                lastBlock[CacheControlProperty] = Ephemeral();
            return true;
        }

        return false;
    }

    private static JsonObject Ephemeral() => new() { ["type"] = "ephemeral" };
}
