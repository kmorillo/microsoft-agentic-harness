using FluentValidation;

namespace Application.AI.Common.CQRS.SkillTraining.SlowUpdate;

/// <summary>Validates <see cref="SlowUpdateCommand"/>.</summary>
public sealed class SlowUpdateCommandValidator : AbstractValidator<SlowUpdateCommand>
{
    /// <summary>Initializes the validator.</summary>
    public SlowUpdateCommandValidator()
    {
        RuleFor(x => x.PriorRollouts).NotEmpty();
        RuleFor(x => x.CurrentRollouts).NotEmpty();
    }
}
