using Domain.AI.Governance;

namespace Application.AI.Common.Interfaces.Governance;

/// <summary>
/// Resolves the Purview sensitivity classification of an external asset so the classification gate can
/// decide whether the agent may touch it. The seam between the harness and a Purview backend
/// (Information Protection or the Data Map).
/// </summary>
/// <remarks>
/// <para>
/// Implementations call out to Purview and should be wrapped in a caching decorator — labels change on
/// scan cadence, not per request, and an uncached lookup on every gated tool call would dominate
/// latency. The default registration is a fail-fast placeholder; real providers are supplied by the
/// Infrastructure layer.
/// </para>
/// <para>
/// Asynchronous and asset-aware by design — unlike the synchronous, content-only
/// <see cref="IResponseSanitizer"/>, a classification lookup is a network call keyed on the asset's
/// identity, not its output text.
/// </para>
/// </remarks>
public interface IDataClassificationProvider
{
    /// <summary>
    /// Resolves the sensitivity label and classifications for an asset.
    /// </summary>
    /// <param name="asset">The asset the agent is about to read from or write to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The resolved label/classifications, or <see cref="AssetLabelResult.Unknown"/> when the asset
    /// cannot be classified (unknown kind, unscanned, or unlabelled). Implementations should return an
    /// unknown result rather than throw when Purview simply has nothing for the asset.
    /// </returns>
    Task<AssetLabelResult> GetLabelAsync(AssetReference asset, CancellationToken cancellationToken);
}
