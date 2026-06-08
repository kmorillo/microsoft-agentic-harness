namespace Domain.AI.Iac;

/// <summary>
/// The outcome of validating and planning an IaC module — <c>terraform validate</c>
/// + <c>terraform plan</c> for Terraform, <c>bicep build</c> for Bicep. Carries
/// enough signal for a gate to decide whether the change is safe to advance.
/// </summary>
public sealed record IacPlanResult
{
    /// <summary>The backend that produced the plan.</summary>
    public required IacBackend Backend { get; init; }

    /// <summary>The module path that was planned (relative to the sandbox working copy).</summary>
    public required string ModulePath { get; init; }

    /// <summary>Whether validation + plan succeeded (the module is syntactically and semantically valid).</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Whether the plan reports any changes to apply. False means the module is already in the desired state.</summary>
    public bool HasChanges { get; init; }

    /// <summary>
    /// Whether the plan includes destructive changes — resource replacement or
    /// deletion. A merge gate must refuse to auto-advance a plan with destructive
    /// changes outside the diff's declared scope.
    /// </summary>
    public bool HasDestructiveChanges { get; init; }

    /// <summary>The raw CLI output of the validate/plan run, retained as gate evidence.</summary>
    public string RawOutput { get; init; } = string.Empty;

    /// <summary>A short human-readable summary of the plan (e.g. "3 to add, 1 to change, 0 to destroy").</summary>
    public string Summary { get; init; } = string.Empty;
}
