namespace Domain.AI.Governance;

/// <summary>
/// Identifies the external asset a tool invocation reads from or writes to, so its sensitivity label
/// can be resolved before the tool runs. This is the <em>subject</em> of a data-classification lookup.
/// </summary>
/// <param name="Type">
/// The kind of asset, which selects the classification backend (or <see cref="AssetType.Unknown"/>
/// when none can answer).
/// </param>
/// <param name="Identifier">
/// The provider-specific identity of the asset — a local file path, a blob/ADLS URI, or a Data Map
/// fully-qualified name. Opaque to the policy layer; meaningful only to the resolving provider.
/// </param>
/// <param name="TenantId">
/// Optional Entra/Purview tenant the asset belongs to, when the deployment spans more than one tenant.
/// Null uses the provider's configured default tenant.
/// </param>
public sealed record AssetReference(
    AssetType Type,
    string Identifier,
    string? TenantId = null)
{
    /// <summary>
    /// Creates an unidentifiable reference — used when a tool's target cannot be mapped to a known
    /// asset kind (arbitrary MCP output, an unsupported data source). Resolves to the configured
    /// unknown-asset policy rather than a real lookup.
    /// </summary>
    /// <param name="identifier">A best-effort identity for audit/trace purposes (may be empty).</param>
    public static AssetReference Unknown(string identifier = "") =>
        new(AssetType.Unknown, identifier);
}
