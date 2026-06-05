using FluentValidation;

namespace Application.AI.Common.CQRS.SkillTraining.GateCandidateSkill;

/// <summary>
/// Validates <see cref="GateCandidateSkillCommand"/> before the gate evaluates it.
/// </summary>
public sealed class GateCandidateSkillCommandValidator : AbstractValidator<GateCandidateSkillCommand>
{
    /// <summary>
    /// Upper bound on any single skill document length. 256 KB is well above the typical
    /// SkillOpt skill artifact (300–2000 tokens) while still catching runaway-rewrite bugs
    /// before they triplicate into the GateResult and downstream audit log.
    /// </summary>
    public const int MaxSkillLength = 262_144;

    /// <summary>Initializes the validator with score / step / weight / length / cross-field rules.</summary>
    public GateCandidateSkillCommandValidator()
    {
        RuleFor(x => x.CandidateSkill)
            .NotNull()
            .MaximumLength(MaxSkillLength)
                .WithMessage($"CandidateSkill exceeds {MaxSkillLength} characters.");

        RuleFor(x => x.CurrentSkill)
            .NotNull()
            .MaximumLength(MaxSkillLength)
                .WithMessage($"CurrentSkill exceeds {MaxSkillLength} characters.");

        RuleFor(x => x.BestSkill)
            .NotNull()
            .MaximumLength(MaxSkillLength)
                .WithMessage($"BestSkill exceeds {MaxSkillLength} characters.");

        RuleFor(x => x.CandidateHard)
            .InclusiveBetween(0.0, 1.0).WithMessage("CandidateHard must be in [0, 1].");

        RuleFor(x => x.CandidateSoft)
            .InclusiveBetween(0.0, 1.0).WithMessage("CandidateSoft must be in [0, 1].");

        RuleFor(x => x.CurrentScore)
            .InclusiveBetween(0.0, 1.0).WithMessage("CurrentScore must be in [0, 1].");

        RuleFor(x => x.BestScore)
            .InclusiveBetween(0.0, 1.0).WithMessage("BestScore must be in [0, 1].");

        RuleFor(x => x.BestStep)
            .GreaterThanOrEqualTo(0).WithMessage("BestStep must be non-negative.");

        RuleFor(x => x.GlobalStep)
            .GreaterThanOrEqualTo(0).WithMessage("GlobalStep must be non-negative.");

        RuleFor(x => x.MixedWeight)
            .InclusiveBetween(0.0, 1.0).WithMessage("MixedWeight must be in [0, 1].");

        // Cross-field: a best snapshot taken at a future step is incoherent — it would
        // mean we accepted a best before it had been evaluated. Almost always signals
        // a checkpoint-resume bug that reset GlobalStep without resetting BestStep.
        RuleFor(x => x)
            .Must(c => c.BestStep <= c.GlobalStep)
            .WithMessage("BestStep must not exceed GlobalStep.");
    }
}
