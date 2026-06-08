namespace Domain.AI.Iac;

/// <summary>
/// Severity classification of an IaC security-scan finding, normalised across
/// the backend scanners (Checkov, tfsec, ARM-TTK) into one ordered scale.
/// </summary>
public enum IacScanSeverity
{
    /// <summary>Informational / style — no security impact.</summary>
    Low = 0,

    /// <summary>A weak default or missing hardening that should be addressed.</summary>
    Medium = 1,

    /// <summary>A misconfiguration with real exposure (e.g. public storage, open security group).</summary>
    High = 2,

    /// <summary>A critical exposure (e.g. plaintext secret, world-writable resource) — must block.</summary>
    Critical = 3
}
