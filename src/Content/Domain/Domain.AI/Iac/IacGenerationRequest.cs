namespace Domain.AI.Iac;

/// <summary>
/// A typed request to scaffold an infrastructure-as-code module. Deterministic —
/// the generator templates a starter module from these fields; it is NOT an LLM
/// call. The agent refines the scaffold afterwards and submits the result as a
/// <c>ChangeProposal</c>.
/// </summary>
public sealed record IacGenerationRequest
{
    /// <summary>The IaC backend to scaffold for (Terraform or Bicep).</summary>
    public required IacBackend Backend { get; init; }

    /// <summary>
    /// The cloud resource type to scaffold, in the backend's own vocabulary
    /// (e.g. <c>azurerm_storage_account</c> for Terraform, <c>Microsoft.Storage/storageAccounts</c>
    /// for Bicep). Required.
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>The logical name for the scaffolded resource (e.g. <c>primary</c>, <c>app_data</c>). Required.</summary>
    public required string ResourceName { get; init; }

    /// <summary>The target environment (<c>dev</c>, <c>staging</c>, <c>prod</c>). Drives default tags and naming.</summary>
    public string Environment { get; init; } = "dev";

    /// <summary>
    /// Optional backend-specific parameters folded into the scaffold (e.g.
    /// <c>location=eastus</c>, <c>sku=Standard_LRS</c>). Keys/values are emitted
    /// verbatim into the templated module.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();
}
