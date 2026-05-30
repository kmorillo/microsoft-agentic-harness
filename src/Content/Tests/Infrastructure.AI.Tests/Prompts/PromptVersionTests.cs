using Domain.AI.Prompts;
using FluentAssertions;
using Xunit;

namespace Infrastructure.AI.Tests.Prompts;

public sealed class PromptVersionTests
{
    [Theory]
    [InlineData("v1", 1, 0)]
    [InlineData("V2", 2, 0)]
    [InlineData("v1.0", 1, 0)]
    [InlineData("v3.7", 3, 7)]
    [InlineData("1.2", 1, 2)]
    [InlineData("4", 4, 0)]
    public void Parse_accepts_v_prefix_and_optional_minor(string text, int expectedMajor, int expectedMinor)
    {
        var v = PromptVersion.Parse(text);
        v.Should().Be(new PromptVersion(expectedMajor, expectedMinor));
    }

    [Theory]
    [InlineData("v")]
    [InlineData("vX")]
    [InlineData("v1.x")]
    [InlineData("1.2.3")]
    public void Parse_rejects_malformed_text(string text)
    {
        var act = () => PromptVersion.Parse(text);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_rejects_empty_or_whitespace_input(string text)
    {
        var act = () => PromptVersion.Parse(text);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryParse_does_not_throw()
    {
        PromptVersion.TryParse("v1.5", out var ok).Should().BeTrue();
        ok.Should().Be(new PromptVersion(1, 5));

        PromptVersion.TryParse("garbage", out _).Should().BeFalse();
    }

    [Fact]
    public void Comparison_orders_by_major_then_minor()
    {
        new PromptVersion(1, 0).Should().BeLessThan(new PromptVersion(1, 1));
        new PromptVersion(1, 99).Should().BeLessThan(new PromptVersion(2, 0));
        new PromptVersion(3, 5).Should().BeGreaterThan(new PromptVersion(3, 4));
        new PromptVersion(2, 2).Should().Be(new PromptVersion(2, 2));
    }

    [Fact]
    public void ToString_renders_v_major_dot_minor()
    {
        new PromptVersion(1, 0).ToString().Should().Be("v1.0");
        new PromptVersion(2, 17).ToString().Should().Be("v2.17");
    }
}
