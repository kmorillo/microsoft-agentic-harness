namespace Domain.AI.Governance;

/// <summary>
/// The outcome of resolving an asset's sensitivity from Purview: the label and classifications found
/// (if any), which surface produced them, and how fresh they are. Immutable value object returned by
/// <c>IDataClassificationProvider</c>.
/// </summary>
/// <param name="Asset">The asset that was looked up.</param>
/// <param name="Label">
/// The resolved sensitivity label, or null when none applies (unlabelled or unknown asset).
/// </param>
/// <param name="Classifications">
/// The classification findings on the asset. Empty when none were found or the asset is unknown.
/// </param>
/// <param name="Source">Which Purview surface produced this result, or <see cref="LabelSource.None"/>.</param>
/// <param name="RetrievedAtUtc">
/// When this result was produced. For Data Map results this reflects the lookup time, not the
/// underlying scan time — see <paramref name="IsStale"/>.
/// </param>
/// <param name="IsStale">
/// Whether the classification metadata is known to be stale. Purview Data Map labels are applied by
/// scheduled scans, so they reflect the last scan rather than live state; a provider sets this when it
/// can determine the backing scan is older than the freshness window. Surfaced so policies and audit
/// can distinguish "verified clean" from "no recent scan".
/// </param>
public sealed record AssetLabelResult(
    AssetReference Asset,
    SensitivityLabel? Label,
    IReadOnlyList<DataClassification> Classifications,
    LabelSource Source,
    DateTimeOffset RetrievedAtUtc,
    bool IsStale = false)
{
    /// <summary>
    /// The classification findings on the asset, never null. A null argument from a provider or binder
    /// is normalized to an empty list so <see cref="HasClassification"/> and downstream readers are safe.
    /// </summary>
    public IReadOnlyList<DataClassification> Classifications { get; init; } = Classifications ?? [];

    /// <summary>
    /// Whether any classification signal (a label or at least one classification) was resolved.
    /// </summary>
    public bool HasClassification => Label is not null || Classifications.Count > 0;

    /// <summary>
    /// Creates a result for an asset Purview could not classify (unknown kind, unscanned, or
    /// unlabelled). Carries <see cref="LabelSource.None"/> and no label.
    /// </summary>
    /// <param name="asset">The asset that was looked up.</param>
    /// <param name="retrievedAtUtc">When the lookup occurred.</param>
    public static AssetLabelResult Unknown(AssetReference asset, DateTimeOffset retrievedAtUtc) =>
        new(asset, null, [], LabelSource.None, retrievedAtUtc);
}
