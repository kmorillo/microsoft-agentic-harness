namespace Domain.AI.Iac;

/// <summary>
/// A single IaC security-scan finding, normalised from a backend scanner
/// (Checkov, tfsec, or ARM-TTK) into a scanner-neutral shape.
/// </summary>
public sealed record IacScanFinding
{
    /// <summary>The scanner that produced the finding (<c>checkov</c>, <c>tfsec</c>, <c>arm-ttk</c>).</summary>
    public required string Scanner { get; init; }

    /// <summary>The scanner's rule / check identifier (e.g. <c>CKV_AZURE_33</c>, <c>azure-storage-default-action-deny</c>).</summary>
    public required string RuleId { get; init; }

    /// <summary>The finding severity, normalised to the shared scale.</summary>
    public required IacScanSeverity Severity { get; init; }

    /// <summary>The resource / template element the finding relates to (e.g. <c>azurerm_storage_account.primary</c>).</summary>
    public string Resource { get; init; } = string.Empty;

    /// <summary>Short, human-readable description of the finding.</summary>
    public string Message { get; init; } = string.Empty;
}
