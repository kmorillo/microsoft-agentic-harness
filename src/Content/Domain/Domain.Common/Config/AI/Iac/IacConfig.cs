namespace Domain.Common.Config.AI.Iac;

/// <summary>
/// Configuration for the IaC skill pack (PR-10). Bound from <c>AppConfig:AI:Iac</c>.
/// Off by default — the skill pack and its tools are inert until <see cref="Enabled"/>
/// is true.
/// </summary>
/// <remarks>
/// <para>
/// The five backend CLIs (terraform, bicep, checkov, tfsec, arm-ttk) run inside
/// the PR-3 sandbox and are pinned to verified versions baked into the sandbox
/// image. The version pins below are advisory metadata surfaced to the runners and
/// validated at boot; they do not install anything.
/// </para>
/// <para>
/// Generation, plan, and scan need outbound access only to the provider/module
/// registries. <see cref="RegistryAllowlist"/> seeds the sandbox egress allowlist
/// for IaC runs; it defaults to the HashiCorp and Microsoft registries.
/// </para>
/// </remarks>
public sealed class IacConfig
{
    /// <summary>Master toggle. When false the skill pack and all IaC tools are inert. Default false.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The backends to enable, by key (<c>"terraform"</c>, <c>"bicep"</c>).
    /// Defaults to both — parity. The startup validator refuses to boot when
    /// enabled with an empty or unknown entry.
    /// </summary>
    public List<string> EnabledBackends { get; set; } = ["terraform", "bicep"];

    /// <summary>Pinned Terraform CLI version (baked into the sandbox image). Validated for non-empty when terraform is enabled.</summary>
    public string TerraformVersion { get; set; } = "1.9.5";

    /// <summary>Pinned Bicep CLI version.</summary>
    public string BicepVersion { get; set; } = "0.30.23";

    /// <summary>Pinned Checkov version (shared by both backends).</summary>
    public string CheckovVersion { get; set; } = "3.2.0";

    /// <summary>Pinned tfsec version (Terraform scanner).</summary>
    public string TfsecVersion { get; set; } = "1.28.11";

    /// <summary>Pinned ARM-TTK version (Bicep scanner).</summary>
    public string ArmTtkVersion { get; set; } = "0.24";

    /// <summary>
    /// The minimum scan-finding severity that blocks a proposal. Findings below
    /// this are reported but do not fail the gate. One of <c>Low</c>, <c>Medium</c>,
    /// <c>High</c>, <c>Critical</c>. Default <c>High</c>.
    /// </summary>
    public string BlockingSeverity { get; set; } = "High";

    /// <summary>
    /// Hosts the IaC sandbox runs may reach (provider/module registries). Seeds the
    /// sandbox egress allowlist for plan/scan runs.
    /// </summary>
    public List<string> RegistryAllowlist { get; set; } =
    [
        "registry.terraform.io",
        "releases.hashicorp.com",
        "mcr.microsoft.com"
    ];
}
