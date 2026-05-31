using Application.AI.Common.CQRS.Prompts.ReplayTraceWithPromptVersion;
using Domain.AI.Prompts;
using Domain.Common.Config.AI;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Application.AI.Common.Tests.CQRS.Prompts;

public sealed class ReplayTraceWithPromptVersionCommandValidatorTests
{
    private readonly ReplayTraceWithPromptVersionCommandValidator _sut = new();

    private static ReplayTraceWithPromptVersionCommand Valid() => new()
    {
        TraceId = "trace-1",
        PromptName = "faithfulness-judge",
        TargetVersion = new PromptVersion(2, 0),
        Variables = new Dictionary<string, object?>(),
        ChatClientType = AIAgentFrameworkClientType.OpenAI,
        Deployment = "gpt-4o-mini",
    };

    [Fact]
    public void Valid_command_has_no_errors()
    {
        var result = _sut.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Missing_TraceId_fails()
    {
        var result = _sut.TestValidate(Valid() with { TraceId = "" });
        result.ShouldHaveValidationErrorFor(c => c.TraceId);
    }

    [Fact]
    public void Missing_PromptName_fails()
    {
        var result = _sut.TestValidate(Valid() with { PromptName = "" });
        result.ShouldHaveValidationErrorFor(c => c.PromptName);
    }

    [Fact]
    public void Missing_Deployment_fails()
    {
        var result = _sut.TestValidate(Valid() with { Deployment = "" });
        result.ShouldHaveValidationErrorFor(c => c.Deployment);
    }

    [Fact]
    public void Negative_MaxOutputTokens_fails()
    {
        var result = _sut.TestValidate(Valid() with { MaxOutputTokens = -1 });
        result.ShouldHaveValidationErrorFor(c => c.MaxOutputTokens);
    }

    [Fact]
    public void Null_MaxOutputTokens_is_allowed()
    {
        var result = _sut.TestValidate(Valid() with { MaxOutputTokens = null });
        result.ShouldNotHaveValidationErrorFor(c => c.MaxOutputTokens);
    }
}
