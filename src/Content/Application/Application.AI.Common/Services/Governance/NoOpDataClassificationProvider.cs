using Application.AI.Common.Interfaces.Governance;
using Domain.AI.Governance;

namespace Application.AI.Common.Services.Governance;

/// <summary>
/// No-op <see cref="IDataClassificationProvider"/> that classifies every asset as
/// <see cref="AssetLabelResult.Unknown"/>. Registered when governance is disabled so the seam resolves
/// without throwing and without contacting Purview.
/// </summary>
/// <remarks>
/// Distinct from <see cref="NotConfiguredDataClassificationProvider"/>: that one throws to signal a
/// misconfiguration (gate on, provider missing); this one returns a benign unknown result so a
/// governance-disabled deployment can resolve the dependency harmlessly. Time is taken from the injected
/// <see cref="TimeProvider"/> so results are deterministic under test.
/// </remarks>
public sealed class NoOpDataClassificationProvider : IDataClassificationProvider
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="NoOpDataClassificationProvider"/> class.</summary>
    /// <param name="timeProvider">Clock used to stamp the unknown result.</param>
    public NoOpDataClassificationProvider(TimeProvider timeProvider) =>
        _timeProvider = timeProvider;

    /// <inheritdoc />
    public Task<AssetLabelResult> GetLabelAsync(AssetReference asset, CancellationToken cancellationToken) =>
        Task.FromResult(AssetLabelResult.Unknown(asset, _timeProvider.GetUtcNow()));
}
