using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class HarnessChangeSuggestionValidatorTests
{
    private readonly HarnessChangeSuggestionValidator _sut = new(new ConfigSurfaceConstraint());

    private static HarnessChangeSuggestion Suggestion(
        HarnessSurface surface = HarnessSurface.ToolErrorRetryLimit,
        string field = ConfigSurfaceConstraint.MaxAttemptsField,
        string proposedValue = "3") => new()
    {
        Surface = surface,
        Field = field,
        CurrentValue = "2",
        ProposedValue = proposedValue,
        Rationale = "transient failures"
    };

    [Fact]
    public void Ctor_NullConstraint_Throws()
    {
        var act = () => new HarnessChangeSuggestionValidator(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_NullSuggestion_Throws()
    {
        var act = () => _sut.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_AllowedFieldInBounds_Allowed()
    {
        var result = _sut.Validate(Suggestion(proposedValue: "4"));

        result.IsAllowed.Should().BeTrue();
        result.RejectionReason.Should().Be(HarnessChangeRejectionReason.None);
        result.NormalizedValue.Should().Be("4");
    }

    [Theory]
    [InlineData(" 3 ", "3")]
    [InlineData("+3", "3")]
    [InlineData("0003", "3")]
    public void Validate_NonCanonicalButInBounds_AllowedWithScrubbedCanonicalValue(string raw, string canonical)
    {
        // Lenient parsing accepts surrounding whitespace / leading sign / leading zeros, but the
        // validated result carries the canonical form so the audit never records the raw proposer string.
        var result = _sut.Validate(Suggestion(proposedValue: raw));

        result.IsAllowed.Should().BeTrue();
        result.NormalizedValue.Should().Be(canonical);
    }

    [Fact]
    public void Validate_Rejected_NormalizedValueIsNull()
    {
        _sut.Validate(Suggestion(proposedValue: "99")).NormalizedValue.Should().BeNull();
    }

    [Fact]
    public void Validate_UngovernedSurface_RejectedWithReason()
    {
        var result = _sut.Validate(Suggestion(surface: HarnessSurface.SkillDocument));

        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().Be(HarnessChangeRejectionReason.UngovernedSurface);
    }

    [Theory]
    [InlineData("BaseDelaySeconds")]
    [InlineData("BackoffType")]
    [InlineData("MaxAttempts ")]   // trailing space — not an exact match
    public void Validate_FrozenOrMistypedField_RejectedFieldNotAllowed(string field)
    {
        var result = _sut.Validate(Suggestion(field: field));

        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().Be(HarnessChangeRejectionReason.FieldNotAllowed);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("3.5")]   // not an integer
    public void Validate_UnparsableValue_RejectedValueUnparsable(string value)
    {
        var result = _sut.Validate(Suggestion(proposedValue: value));

        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().Be(HarnessChangeRejectionReason.ValueUnparsable);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("6")]
    [InlineData("100")]
    [InlineData("-3")]
    public void Validate_OutOfBoundsValue_RejectedValueOutOfBounds(string value)
    {
        var result = _sut.Validate(Suggestion(proposedValue: value));

        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().Be(HarnessChangeRejectionReason.ValueOutOfBounds);
    }

    [Fact]
    public void Validate_ChecksSurfaceBeforeField()
    {
        // Wrong surface AND wrong field — surface is checked first, so the reason is UngovernedSurface.
        var result = _sut.Validate(Suggestion(surface: HarnessSurface.SkillDocument, field: "BaseDelaySeconds"));

        result.RejectionReason.Should().Be(HarnessChangeRejectionReason.UngovernedSurface);
    }
}
