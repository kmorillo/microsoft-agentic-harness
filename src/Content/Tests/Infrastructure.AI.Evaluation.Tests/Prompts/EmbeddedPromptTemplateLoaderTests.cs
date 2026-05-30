using FluentAssertions;
using Infrastructure.AI.Evaluation.Prompts;
using Xunit;

namespace Infrastructure.AI.Evaluation.Tests.Prompts;

public sealed class EmbeddedPromptTemplateLoaderTests
{
    private readonly EmbeddedPromptTemplateLoader _sut = new();

    [Theory]
    [InlineData("faithfulness")]
    [InlineData("context-precision")]
    [InlineData("context-recall")]
    [InlineData("answer-relevance")]
    [InlineData("answer-correctness")]
    public void Load_returns_non_empty_body_with_frontmatter_stripped_for_each_shipped_template(string name)
    {
        var body = _sut.Load(name);

        body.Should().NotBeNullOrWhiteSpace();
        // Frontmatter has been stripped — no leading `---` block remains.
        body.TrimStart().Should().NotStartWith("---");
        // Each template uses the JSON-only response instruction.
        body.Should().Contain("score");
    }

    [Fact]
    public void Load_caches_template_so_repeated_loads_return_same_instance()
    {
        var first = _sut.Load("faithfulness");
        var second = _sut.Load("faithfulness");

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void Load_throws_FileNotFoundException_for_unknown_template()
    {
        var act = () => _sut.Load("does-not-exist");

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*does-not-exist*");
    }

    [Theory]
    [InlineData("---\nname: x\n---\nbody-line-1\n---\nbody-line-2\n", "body-line-1\n---\nbody-line-2\n", "interior `---` line is body, not fence close")]
    [InlineData("---\nname: x\n---\n# Body\n------\nstill body\n", "# Body\n------\nstill body\n", "`------` is not a pure `---` line")]
    [InlineData("---START not real frontmatter\nplain body\n", "---START not real frontmatter\nplain body\n", "opening must be pure `---`")]
    [InlineData("---\nname: x\nno_close_fence_here\n", "---\nname: x\nno_close_fence_here\n", "unmatched open returns raw")]
    public void StripFrontmatter_only_consumes_pure_dashdashdash_lines(string raw, string expectedBody, string scenario)
    {
        var loaderType = typeof(Infrastructure.AI.Evaluation.Prompts.EmbeddedPromptTemplateLoader);
        var method = loaderType.GetMethod("StripFrontmatter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var actual = (string)method.Invoke(null, new object[] { raw })!;

        actual.Should().Be(expectedBody, scenario);
    }
}
