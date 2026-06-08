namespace Domain.AI.Iac;

/// <summary>
/// The aggregated outcome of running the backend's security scanners over an IaC
/// module — Checkov + tfsec for Terraform, ARM-TTK + Checkov for Bicep.
/// </summary>
public sealed record IacScanResult
{
    /// <summary>The backend whose module was scanned.</summary>
    public required IacBackend Backend { get; init; }

    /// <summary>The module path that was scanned (relative to the sandbox working copy).</summary>
    public required string ModulePath { get; init; }

    /// <summary>
    /// Whether the scan passed the gate's policy. Convention: pass when there are
    /// no findings at or above the configured blocking severity (default High).
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>The scanners that ran (e.g. <c>["checkov", "tfsec"]</c>). Empty means no scanner produced output.</summary>
    public IReadOnlyList<string> ScannersRun { get; init; } = [];

    /// <summary>All normalised findings across the scanners that ran.</summary>
    public IReadOnlyList<IacScanFinding> Findings { get; init; } = [];
}
