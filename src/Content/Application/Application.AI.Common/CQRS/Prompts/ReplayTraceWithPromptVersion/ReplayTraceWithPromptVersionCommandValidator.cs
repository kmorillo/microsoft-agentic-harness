using FluentValidation;

namespace Application.AI.Common.CQRS.Prompts.ReplayTraceWithPromptVersion;

/// <summary>
/// Validates <see cref="ReplayTraceWithPromptVersionCommand"/> before execution.
/// Catches missing required fields before any registry or LLM dispatch.
/// </summary>
public sealed class ReplayTraceWithPromptVersionCommandValidator : AbstractValidator<ReplayTraceWithPromptVersionCommand>
{
    /// <summary>Initializes the validator with rules for command shape.</summary>
    public ReplayTraceWithPromptVersionCommandValidator()
    {
        RuleFor(x => x.TraceId)
            .NotEmpty().WithMessage("TraceId is required.");

        RuleFor(x => x.PromptName)
            .NotEmpty().WithMessage("PromptName is required.");

        RuleFor(x => x.Deployment)
            .NotEmpty().WithMessage("Deployment is required.");

        RuleFor(x => x.Variables)
            .NotNull().WithMessage("Variables dictionary is required (supply an empty dictionary when the prompt has no placeholders).");

        RuleFor(x => x.MaxOutputTokens)
            .GreaterThan(0).When(x => x.MaxOutputTokens.HasValue)
            .WithMessage("MaxOutputTokens must be positive when supplied.");
    }
}
