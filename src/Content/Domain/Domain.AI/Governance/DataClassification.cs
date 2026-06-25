namespace Domain.AI.Governance;

/// <summary>
/// A single classification finding on an asset — a logical category of sensitive data Purview detected
/// (for example "Credit Card Number" or "Person's Name"). Distinct from a
/// <see cref="SensitivityLabel"/>: classifications describe <em>what kinds of data</em> an asset
/// contains, while a label is the overall sensitivity tag (often derived from classifications).
/// </summary>
/// <param name="Name">The classification name, system-defined or custom.</param>
/// <param name="Confidence">
/// The provider's confidence in the finding, 0.0 to 1.0. Data Map scans report a 0–100 confidence which
/// callers normalize into this range.
/// </param>
/// <param name="IsCustom">
/// Whether this is a custom (organization-defined) classification rather than a Microsoft built-in.
/// </param>
public sealed record DataClassification(
    string Name,
    double Confidence = 1.0,
    bool IsCustom = false);
