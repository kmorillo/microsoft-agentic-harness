using System.Text.Json.Serialization;

namespace Infrastructure.AI.Governance.Classification;

/// <summary>
/// Wire-format envelope for an Apache Atlas <c>get entity by unique attribute</c> response from the
/// Microsoft Purview Data Map — <c>GET /catalog/api/atlas/v2/entity/uniqueAttribute/type/{typeName}</c>.
/// Only the single resolved <see cref="Entity"/> is bound; the response's <c>referredEntities</c> map is
/// ignored. Internal to the Data Map provider.
/// </summary>
/// <param name="Entity">
/// The resolved catalog entity, or null when the body did not carry one (an unrecognized shape).
/// </param>
internal sealed record AtlasEntityWithExtInfo(
    [property: JsonPropertyName("entity")] AtlasEntity? Entity);

/// <summary>
/// Wire-format projection of an Apache Atlas entity as the Purview Data Map returns it. Only the fields
/// the classification gate needs — the applied sensitivity labels, the scan classifications, and the
/// last-update time used to judge staleness — are bound; the rest of the entity is ignored.
/// </summary>
/// <param name="Labels">
/// The asset's applied label tags. In a Purview Data Map deployment the applied sensitivity label surfaces
/// here; the provider treats the first as the asset's sensitivity label. Null when the asset carries none.
/// </param>
/// <param name="Classifications">
/// The classification findings a scan attached to the asset (for example a credit-card or SSN
/// classification). Carried as audit detail. Null when the asset carries none.
/// </param>
/// <param name="UpdateTime">
/// The catalog entry's last-update time as Unix epoch milliseconds, used to judge whether the scanned
/// metadata is stale. Null or zero when the Data Map did not report one.
/// </param>
internal sealed record AtlasEntity(
    [property: JsonPropertyName("labels")] IReadOnlyList<string>? Labels,
    [property: JsonPropertyName("classifications")] IReadOnlyList<AtlasClassification>? Classifications,
    [property: JsonPropertyName("updateTime")] long? UpdateTime);

/// <summary>
/// Wire-format projection of a single Apache Atlas classification on an entity. Only the classification's
/// type name — the human-readable finding name — is bound. Internal to the Data Map provider.
/// </summary>
/// <param name="TypeName">The classification's type name, surfaced as a <c>DataClassification</c> finding.</param>
internal sealed record AtlasClassification(
    [property: JsonPropertyName("typeName")] string? TypeName);
