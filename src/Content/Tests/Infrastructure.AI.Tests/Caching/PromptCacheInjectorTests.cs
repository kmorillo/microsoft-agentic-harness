using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Infrastructure.AI.Caching;
using Xunit;

namespace Infrastructure.AI.Tests.Caching;

/// <summary>
/// Tests for <see cref="PromptCacheInjector"/> — the pure transform that stamps an Anthropic
/// prompt-cache breakpoint onto the system message of an OpenAI-format chat-completions body.
/// </summary>
public sealed class PromptCacheInjectorTests
{
    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void InjectSystemCacheControl_StringSystemContent_BecomesCachedTextArray()
    {
        const string input = """
        {"model":"anthropic/claude-sonnet-4.6","messages":[
            {"role":"system","content":"You are a helpful agent."},
            {"role":"user","content":"Hi"}
        ]}
        """;

        var result = Parse(PromptCacheInjector.InjectSystemCacheControl(input));

        var systemContent = result["messages"]![0]!["content"]!.AsArray();
        systemContent.Should().HaveCount(1);
        systemContent[0]!["type"]!.GetValue<string>().Should().Be("text");
        systemContent[0]!["text"]!.GetValue<string>().Should().Be("You are a helpful agent.");
        systemContent[0]!["cache_control"]!["type"]!.GetValue<string>().Should().Be("ephemeral");
    }

    [Fact]
    public void InjectSystemCacheControl_ArraySystemContent_MarksLastPart()
    {
        const string input = """
        {"messages":[
            {"role":"system","content":[
                {"type":"text","text":"Stable tools preamble."},
                {"type":"text","text":"Stable system prompt."}
            ]},
            {"role":"user","content":"Hi"}
        ]}
        """;

        var result = Parse(PromptCacheInjector.InjectSystemCacheControl(input));

        var parts = result["messages"]![0]!["content"]!.AsArray();
        parts[0]!["cache_control"].Should().BeNull("only the last block carries the breakpoint");
        parts[1]!["cache_control"]!["type"]!.GetValue<string>().Should().Be("ephemeral");
    }

    [Fact]
    public void InjectSystemCacheControl_LastSystemMessageIsMarked_NotTheFirst()
    {
        const string input = """
        {"messages":[
            {"role":"system","content":"First."},
            {"role":"user","content":"Hi"},
            {"role":"system","content":"Second."}
        ]}
        """;

        var result = Parse(PromptCacheInjector.InjectSystemCacheControl(input));

        // First system message untouched (still a plain string); only the last is marked.
        result["messages"]![0]!["content"]!.GetValue<string>().Should().Be("First.");
        result["messages"]![2]!["content"]!.AsArray()[0]!["cache_control"]!["type"]!
            .GetValue<string>().Should().Be("ephemeral");
    }

    [Fact]
    public void InjectSystemCacheControl_NoSystemMessage_ReturnsUnchanged()
    {
        const string input = """{"messages":[{"role":"user","content":"Hi"}]}""";

        var result = PromptCacheInjector.InjectSystemCacheControl(input);

        result.Should().NotContain("cache_control");
    }

    [Fact]
    public void InjectSystemCacheControl_AlreadyMarked_IsIdempotent()
    {
        const string input = """
        {"messages":[
            {"role":"system","content":[
                {"type":"text","text":"Stable.","cache_control":{"type":"ephemeral"}}
            ]}
        ]}
        """;

        var once = PromptCacheInjector.InjectSystemCacheControl(input);
        var twice = PromptCacheInjector.InjectSystemCacheControl(once);

        // Exactly one breakpoint survives a second pass — no nesting or duplication.
        CountOccurrences(twice, "cache_control").Should().Be(1);
    }

    [Fact]
    public void InjectSystemCacheControl_InvalidJson_ReturnsOriginalUnchanged()
    {
        const string input = "not json at all {";

        var result = PromptCacheInjector.InjectSystemCacheControl(input);

        result.Should().Be(input);
    }

    [Fact]
    public void InjectSystemCacheControl_UserContentUntouched()
    {
        const string input = """
        {"messages":[
            {"role":"system","content":"Sys."},
            {"role":"user","content":"User stays a plain string."}
        ]}
        """;

        var result = Parse(PromptCacheInjector.InjectSystemCacheControl(input));

        result["messages"]![1]!["content"]!.GetValue<string>().Should().Be("User stays a plain string.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) != -1) { count++; i += needle.Length; }
        return count;
    }
}
