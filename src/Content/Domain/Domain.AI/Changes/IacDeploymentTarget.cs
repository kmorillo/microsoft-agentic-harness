namespace Domain.AI.Changes;

/// <summary>
/// A <see cref="ChangeTarget"/> identifying an infrastructure-as-code deployment —
/// a Terraform module or Bicep deployment that produces real cloud resources.
/// </summary>
/// <remarks>
/// <para>
/// The <c>MergeGate</c> for this target uses the configured <c>IIacGenerator</c>
/// (PR-10) to drive the IaC backend's plan/apply cycle. The applier is responsible
/// for capturing the plan output as evidence and refusing to proceed if the plan
/// shows unexpected destructive changes (resource replacement, deletion of resources
/// outside the diff's scope).
/// </para>
/// <para>
/// <see cref="Backend"/> identifies which IaC backend is used (typically <c>terraform</c>
/// or <c>bicep</c>). This drives which <c>IIacGenerator</c> implementation is resolved
/// from keyed DI when the merge gate runs.
/// </para>
/// </remarks>
public sealed class IacDeploymentTarget : ChangeTarget
{
    /// <summary>
    /// Construct an <see cref="IacDeploymentTarget"/>.
    /// </summary>
    /// <param name="backend">The IaC backend identifier (e.g. <c>terraform</c>, <c>bicep</c>). Drives keyed-DI resolution of the applier.</param>
    /// <param name="deploymentName">The deployment name within the backend (Terraform workspace name or Bicep deployment name).</param>
    /// <param name="modulePath">The path to the IaC module/template being modified (e.g. <c>modules/network/main.tf</c>, <c>infra/main.bicep</c>).</param>
    /// <param name="environment">The target environment (e.g. <c>dev</c>, <c>staging</c>, <c>prod</c>). Drives default blast radius — prod is at least High.</param>
    public IacDeploymentTarget(
        string backend,
        string deploymentName,
        string modulePath,
        string environment)
        : base(ChangeTargetKind.IacDeployment, BuildDisplayName(backend, deploymentName, environment))
    {
        Backend = backend ?? string.Empty;
        DeploymentName = deploymentName ?? string.Empty;
        ModulePath = modulePath ?? string.Empty;
        Environment = environment ?? string.Empty;
    }

    /// <summary>The IaC backend identifier (e.g. <c>terraform</c>, <c>bicep</c>).</summary>
    public string Backend { get; }

    /// <summary>The deployment name within the backend (Terraform workspace, Bicep deployment).</summary>
    public string DeploymentName { get; }

    /// <summary>The path to the IaC module or template being modified.</summary>
    public string ModulePath { get; }

    /// <summary>The target environment (<c>dev</c>, <c>staging</c>, <c>prod</c>).</summary>
    public string Environment { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Canonical form: <c>iac:{backend}:{environment}:{deploymentName}:{modulePath}</c>.
    /// </remarks>
    public override string CanonicalKey() =>
        $"iac:{Backend}:{Environment}:{DeploymentName}:{ModulePath}";

    private static string BuildDisplayName(string backend, string deploymentName, string environment)
    {
        if (string.IsNullOrEmpty(deploymentName))
        {
            return "(unspecified iac target)";
        }

        var prefix = string.IsNullOrEmpty(backend) ? "iac" : backend;
        var env = string.IsNullOrEmpty(environment) ? "?" : environment;
        return $"{prefix}/{env}/{deploymentName}";
    }
}
