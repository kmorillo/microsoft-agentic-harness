using Application.AI.Common.Interfaces.Governance;
using Domain.AI.Governance;

namespace Application.AI.Common.Services.Governance;

/// <summary>
/// Fail-fast placeholder <see cref="IDataClassificationProvider"/>. Throws with explicit guidance when
/// invoked — the classification seam ships with this default so DI can resolve the provider, but a real
/// Purview-backed implementation is wiring the consumer owns.
/// </summary>
/// <remarks>
/// Registered as the default when governance is enabled. It is only ever invoked when classification
/// enforcement is switched on (<c>DataClassificationConfig.Mode</c> is not <c>Off</c>) yet no real
/// provider has been registered — exactly the "you enabled the gate but forgot to wire Purview" case,
/// where a loud failure is correct. With the default <c>Mode = Off</c> the gate never consults the
/// provider, so this never throws. Replace via a replacement registration (e.g. the Graph or Data Map
/// client in Infrastructure) before enabling classification.
/// </remarks>
public sealed class NotConfiguredDataClassificationProvider : IDataClassificationProvider
{
    /// <inheritdoc />
    public Task<AssetLabelResult> GetLabelAsync(AssetReference asset, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "Data classification is enabled (DataClassificationConfig.Mode is not Off) but no " +
            "IDataClassificationProvider is configured. Register a Purview-backed provider " +
            "(e.g. GraphSensitivityLabelClient or PurviewDataMapClient in Infrastructure.AI.Governance) " +
            "before enabling classification enforcement.");
}
