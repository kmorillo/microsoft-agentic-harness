using Domain.AI.Governance;
using FluentAssertions;
using Xunit;

namespace Domain.AI.Tests.Governance;

/// <summary>
/// Tests for <see cref="AssetLabelResult"/> — the <see cref="AssetLabelResult.Unknown"/> factory and the
/// <see cref="AssetLabelResult.HasClassification"/> projection that distinguishes "classified" from
/// "nothing resolved".
/// </summary>
public class AssetLabelResultTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly AssetReference SampleAsset = new(AssetType.AzureBlob, "blob://x");

    [Fact]
    public void Unknown_ProducesNoneSourceWithNoLabelOrClassifications()
    {
        var result = AssetLabelResult.Unknown(SampleAsset, Now);

        result.Source.Should().Be(LabelSource.None);
        result.Label.Should().BeNull();
        result.Classifications.Should().BeEmpty();
        result.RetrievedAtUtc.Should().Be(Now);
        result.IsStale.Should().BeFalse();
        result.HasClassification.Should().BeFalse();
    }

    [Fact]
    public void HasClassification_IsTrue_WhenLabelPresent()
    {
        var result = new AssetLabelResult(
            SampleAsset, new SensitivityLabel("id", "Confidential"), [], LabelSource.DataMap, Now);

        result.HasClassification.Should().BeTrue();
    }

    [Fact]
    public void HasClassification_IsTrue_WhenClassificationsPresentWithoutLabel()
    {
        var result = new AssetLabelResult(
            SampleAsset, Label: null, [new DataClassification("Person's Name")], LabelSource.DataMap, Now);

        result.HasClassification.Should().BeTrue();
    }
}
