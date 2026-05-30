using System.Text.Json;
using Application.AI.Common.Json;
using FluentAssertions;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Json;

public sealed class LlmJsonResponseParserTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private sealed record Probe
    {
        public double Score { get; init; }
        public string? Reasoning { get; init; }
    }

    [Fact]
    public void TryParseObject_extracts_balanced_payload_when_prose_contains_stray_braces()
    {
        var raw = "Sure! Here is my {final} verdict: {\"score\":0.8,\"reasoning\":\"good\"}";

        var ok = LlmJsonResponseParser.TryParseObject<Probe>(raw, CaseInsensitive, out var probe);

        ok.Should().BeTrue();
        probe!.Score.Should().Be(0.8);
        probe.Reasoning.Should().Be("good");
    }

    [Fact]
    public void TryParseObject_ignores_braces_inside_string_literals()
    {
        var raw = "{\"score\":1.0,\"reasoning\":\"this } is { tricky\"}";

        var ok = LlmJsonResponseParser.TryParseObject<Probe>(raw, CaseInsensitive, out var probe);

        ok.Should().BeTrue();
        probe!.Reasoning.Should().Be("this } is { tricky");
    }

    [Fact]
    public void TryParseObject_returns_false_on_unbalanced_input()
    {
        var raw = "{\"score\":0.5";

        var ok = LlmJsonResponseParser.TryParseObject<Probe>(raw, CaseInsensitive, out var probe);

        ok.Should().BeFalse();
        probe.Should().BeNull();
    }

    [Fact]
    public void TryParseObject_handles_fenced_block_with_language_tag()
    {
        var raw = "```json\n{\"score\":0.5,\"reasoning\":\"x\"}\n```";

        var ok = LlmJsonResponseParser.TryParseObject<Probe>(raw, CaseInsensitive, out var probe);

        ok.Should().BeTrue();
        probe!.Score.Should().Be(0.5);
    }

    [Fact]
    public void TryParseObject_handles_prose_preamble_before_fence()
    {
        var raw = "Here is the JSON:\n```json\n{\"score\":0.5,\"reasoning\":\"x\"}\n```";

        var ok = LlmJsonResponseParser.TryParseObject<Probe>(raw, CaseInsensitive, out var probe);

        // Even though StripFences only strips a fence at the very start, the
        // brace-scan recovers the payload when prose precedes the fence.
        ok.Should().BeTrue();
        probe!.Score.Should().Be(0.5);
    }

    [Fact]
    public void TryParseArray_extracts_first_balanced_array()
    {
        var raw = "[{\"a\":1,\"b\":[2,3]},{\"c\":4}]";

        var ok = LlmJsonResponseParser.TryParseArray<List<JsonElement>>(raw, CaseInsensitive, out var arr);

        ok.Should().BeTrue();
        arr!.Should().HaveCount(2);
    }

    [Fact]
    public void StripFences_returns_input_unchanged_when_no_fence_present()
    {
        var raw = "no fences here {\"x\":1}";
        LlmJsonResponseParser.StripFences(raw).Should().Be(raw);
    }
}
