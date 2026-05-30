using Domain.AI.Prompts;
using FluentAssertions;
using Infrastructure.AI.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Prompts;

public sealed class ScribanPromptRendererTests
{
    private static PromptDescriptor Desc(string body) => new()
    {
        Name = "test",
        Version = new PromptVersion(1, 0),
        ContentHash = "deadbeef",
        Body = body
    };

    private readonly ScribanPromptRenderer _sut = new(NullLogger<ScribanPromptRenderer>.Instance);

    [Fact]
    public async Task Renders_simple_variable_substitution_and_html_escapes_values()
    {
        var d = Desc("Hello {{ name }}!");
        var vars = new Dictionary<string, object?> { ["name"] = "<world>" };

        var result = await _sut.RenderAsync(d, vars, CancellationToken.None);

        result.Body.Should().Be("Hello &lt;world&gt;!");
        result.Source.Should().BeSameAs(d);
        result.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Reports_unresolved_variables_without_failing()
    {
        var d = Desc("Hi {{ name }} from {{ place }}");
        var vars = new Dictionary<string, object?> { ["name"] = "Bob" };

        var result = await _sut.RenderAsync(d, vars, CancellationToken.None);

        result.Body.Should().Contain("Bob");
        result.Unresolved.Should().Contain("place");
    }

    [Fact]
    public async Task Non_string_value_uses_default_to_string_without_html_escape()
    {
        var d = Desc("Count={{ n }}");
        var vars = new Dictionary<string, object?> { ["n"] = 42 };

        var result = await _sut.RenderAsync(d, vars, CancellationToken.None);

        result.Body.Should().Be("Count=42");
    }

    [Fact]
    public async Task Parse_error_throws_invalid_operation_with_descriptor_identifier()
    {
        // Use a deliberately bad token sequence Scriban rejects: an isolated `?`
        // operator with no operand triggers a parser error reliably.
        var d = Desc("{{ ? }}");

        Func<Task> act = () => _sut.RenderAsync(d, new Dictionary<string, object?>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Scriban parse error*test*");
    }

    [Fact]
    public async Task Handles_no_variables_dictionary_entries()
    {
        var d = Desc("just text");
        var result = await _sut.RenderAsync(d, new Dictionary<string, object?>(), CancellationToken.None);
        result.Body.Should().Be("just text");
    }
}
