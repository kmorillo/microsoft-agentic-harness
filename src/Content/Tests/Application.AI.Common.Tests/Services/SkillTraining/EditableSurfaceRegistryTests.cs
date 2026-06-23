using Application.AI.Common.Services.SkillTraining;
using Domain.AI.SkillTraining;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Services.SkillTraining;

public class EditableSurfaceRegistryTests
{
    private static readonly EditableSurfaceRegistry Default = new();

    [Fact]
    public void Default_OnlySkillDocument_IsEditable()
    {
        Default.IsEditable(HarnessSurface.SkillDocument).Should().BeTrue();
    }

    [Theory]
    [InlineData(HarnessSurface.SystemPrompt)]
    [InlineData(HarnessSurface.ArtifactGuidance)]
    [InlineData(HarnessSurface.FailureRecovery)]
    [InlineData(HarnessSurface.VerificationPrompt)]
    [InlineData(HarnessSurface.ToolErrorRetryLimit)]
    [InlineData(HarnessSurface.ToolAvailability)]
    [InlineData(HarnessSurface.MemoryScopeRules)]
    [InlineData(HarnessSurface.DeniedTools)]
    [InlineData(HarnessSurface.AutonomyTier)]
    [InlineData(HarnessSurface.ContentSafetyConfig)]
    [InlineData(HarnessSurface.EditableSurfaceRegistry)]
    public void Default_EveryNonSkillSurface_IsFrozen(HarnessSurface surface)
    {
        Default.IsEditable(surface).Should().BeFalse();
    }

    [Theory]
    [InlineData(HarnessSurface.DeniedTools)]
    [InlineData(HarnessSurface.AutonomyTier)]
    [InlineData(HarnessSurface.ContentSafetyConfig)]
    [InlineData(HarnessSurface.EditableSurfaceRegistry)]
    public void IsFrozenByConstruction_True_ForGovernanceSurfaces(HarnessSurface surface)
    {
        Default.IsFrozenByConstruction(surface).Should().BeTrue();
    }

    [Theory]
    [InlineData(HarnessSurface.SkillDocument)]
    [InlineData(HarnessSurface.SystemPrompt)]
    [InlineData(HarnessSurface.ToolAvailability)]
    [InlineData(HarnessSurface.MemoryScopeRules)]
    public void IsFrozenByConstruction_False_ForNonGovernanceSurfaces(HarnessSurface surface)
    {
        // Note: ToolAvailability and MemoryScopeRules are frozen *today* but only by policy — a future
        // phase could unfreeze them. The by-construction freeze is reserved for hard governance.
        Default.IsFrozenByConstruction(surface).Should().BeFalse();
    }

    [Fact]
    public void WideningCtor_CanMarkLowStakeSurfaceEditable()
    {
        var widened = new EditableSurfaceRegistry(
            [HarnessSurface.SkillDocument, HarnessSurface.FailureRecovery]);

        widened.IsEditable(HarnessSurface.SkillDocument).Should().BeTrue();
        widened.IsEditable(HarnessSurface.FailureRecovery).Should().BeTrue();
        widened.IsEditable(HarnessSurface.SystemPrompt).Should().BeFalse();
    }

    [Theory]
    [InlineData(HarnessSurface.DeniedTools)]
    [InlineData(HarnessSurface.AutonomyTier)]
    [InlineData(HarnessSurface.ContentSafetyConfig)]
    [InlineData(HarnessSurface.EditableSurfaceRegistry)]
    public void WideningCtor_Throws_WhenAskedToUnfreezeByConstructionSurface(HarnessSurface frozen)
    {
        var act = () => new EditableSurfaceRegistry([HarnessSurface.SkillDocument, frozen]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*frozen by construction*");
    }

    [Fact]
    public void WideningCtor_NullArgument_Throws()
    {
        var act = () => new EditableSurfaceRegistry(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
