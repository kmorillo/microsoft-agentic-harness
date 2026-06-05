using FluentValidation;

namespace Application.AI.Common.CQRS.SkillTraining.ReflectOnFailures;

/// <summary>
/// Validates <see cref="ReflectOnFailuresCommand"/>. Catches empty rollout batches and
/// malformed entries before the proposer is engaged (proposer calls cost real LLM tokens).
/// </summary>
public sealed class ReflectOnFailuresCommandValidator : AbstractValidator<ReflectOnFailuresCommand>
{
    /// <summary>Initializes the validator.</summary>
    public ReflectOnFailuresCommandValidator()
    {
        // Empty/whitespace skill = nothing for the optimizer to anchor against.
        // The proposer cost is non-trivial; reject early.
        RuleFor(x => x.CurrentSkill)
            .NotEmpty().WithMessage("CurrentSkill must be a non-empty skill document.");

        RuleFor(x => x.Rollouts)
            .NotEmpty().WithMessage("At least one rollout is required.");

        RuleForEach(x => x.Rollouts).ChildRules(r =>
        {
            r.RuleFor(rollout => rollout.ItemId)
                .NotEmpty().WithMessage("Rollout ItemId must not be empty.");

            r.RuleFor(rollout => rollout.Hard)
                .InclusiveBetween(0.0, 1.0).WithMessage("Rollout Hard must be in [0, 1].");

            r.RuleFor(rollout => rollout.Soft)
                .InclusiveBetween(0.0, 1.0).WithMessage("Rollout Soft must be in [0, 1].");
        });

        // When the caller suppresses successes, at least one failure must remain for the
        // proposer to reflect on — otherwise the post-filter list is empty and the LLM
        // call is guaranteed-wasted budget. Catch this at validation rather than at the
        // proposer boundary.
        RuleFor(x => x)
            .Must(c => c.IncludeSuccesses || c.Rollouts.Any(r => !r.IsSuccess))
            .WithMessage(
                "When IncludeSuccesses is false, at least one rollout must be a failure (Hard < 1.0).");
    }
}
