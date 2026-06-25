using Application.AI.Common.Services.Governance;
using Domain.AI.Governance;
using FluentAssertions;
using Xunit;

namespace Application.AI.Common.Tests.Governance;

/// <summary>
/// Tests for the two fail-safe <c>IDataClassificationProvider</c> defaults: the no-op provider that
/// classifies everything as Unknown (governance-disabled path), and the not-configured provider that
/// throws to flag a "gate enabled, Purview not wired" misconfiguration.
/// </summary>
public class DataClassificationProviderDefaultsTests
{
    private static readonly AssetReference SampleAsset = new(AssetType.LocalFile, "/tmp/x.txt");

    [Fact]
    public async Task NoOp_ReturnsUnknownStampedWithProviderClock()
    {
        var now = new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero);
        var provider = new NoOpDataClassificationProvider(new FixedTimeProvider(now));

        var result = await provider.GetLabelAsync(SampleAsset, CancellationToken.None);

        result.Source.Should().Be(LabelSource.None);
        result.HasClassification.Should().BeFalse();
        result.RetrievedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task NotConfigured_ThrowsWithGuidance()
    {
        var provider = new NotConfiguredDataClassificationProvider();

        var act = () => provider.GetLabelAsync(SampleAsset, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("IDataClassificationProvider");
    }

    /// <summary>
    /// Minimal <see cref="TimeProvider"/> shim returning a fixed UTC time. Mirrors the project
    /// convention of a local shim rather than referencing Microsoft.Extensions.TimeProvider.Testing.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
