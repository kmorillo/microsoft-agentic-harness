using Application.AI.Common.Evaluation;
using FluentAssertions;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Prompts;

public sealed class PromptTemplateRendererTests
{
    [Fact]
    public void Render_substitutes_known_placeholders_and_html_escapes_values()
    {
        var template = "Question: {{q}} Answer: {{a}}";
        var vars = new Dictionary<string, string?> { ["q"] = "<x>", ["a"] = "&y" };

        var rendered = PromptTemplateRenderer.Render(template, vars, out var unresolved);

        rendered.Should().Be("Question: &lt;x&gt; Answer: &amp;y");
        unresolved.Should().BeEmpty();
    }

    [Fact]
    public void Render_leaves_unknown_placeholders_in_place_and_reports_them()
    {
        var template = "Hello {{name}} from {{place}}.";
        var vars = new Dictionary<string, string?> { ["name"] = "Bob" };

        var rendered = PromptTemplateRenderer.Render(template, vars, out var unresolved);

        rendered.Should().Be("Hello Bob from {{place}}.");
        unresolved.Should().ContainSingle().Which.Should().Be("place");
    }

    [Fact]
    public void Render_handles_null_variable_value_as_empty_string()
    {
        var template = "X={{x}}";
        var vars = new Dictionary<string, string?> { ["x"] = null };

        var rendered = PromptTemplateRenderer.Render(template, vars, out _);

        rendered.Should().Be("X=");
    }

    [Fact]
    public void Render_passes_through_template_with_no_placeholders()
    {
        var template = "just plain text — no braces here";
        var rendered = PromptTemplateRenderer.Render(template, new Dictionary<string, string?>(), out var unresolved);

        rendered.Should().Be(template);
        unresolved.Should().BeEmpty();
    }

    [Fact]
    public void Render_does_not_recurse_when_substituted_value_contains_placeholders()
    {
        var template = "{{x}}";
        var vars = new Dictionary<string, string?> { ["x"] = "{{x}}" };

        var rendered = PromptTemplateRenderer.Render(template, vars, out _);

        // HtmlEncode preserves braces verbatim; no second-pass expansion.
        rendered.Should().Be("{{x}}");
    }

    [Fact]
    public void Render_trims_whitespace_inside_braces()
    {
        var template = "{{  name  }} hi";
        var vars = new Dictionary<string, string?> { ["name"] = "Bob" };

        PromptTemplateRenderer.Render(template, vars, out _).Should().Be("Bob hi");
    }

    [Fact]
    public void Render_does_not_crash_on_unmatched_open_braces()
    {
        var template = "Trailing {{open";
        var rendered = PromptTemplateRenderer.Render(template, new Dictionary<string, string?>(), out var unresolved);

        rendered.Should().Be("Trailing {{open");
        unresolved.Should().BeEmpty();
    }
}
