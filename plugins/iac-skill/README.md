# IaC Skill Pack

Scaffold, validate, plan, and security-scan **Terraform** and **Bicep**
infrastructure-as-code — without ever deploying.

## Design principles

- **Plan and scan only — never deploy.** No skill in this pack can run
  `terraform apply`, `az deployment create`, or `kubectl apply`. Those tools are
  explicitly denied on every skill and the denial is bypass-immune. The skill
  produces a plan + a scan verdict; applying the change is the gate pipeline's job.
- **Fixes flow through ChangeProposal.** The generator scaffolds files and the
  agent submits them as a `ChangeProposal` against the configured Git repository.
  The IaC validator (`iac_plan_scan`) runs inside the `SelfValidationGate` and
  blocks any proposal whose plan errors, shows unexpected destructive changes, or
  fails the security scan.
- **Backend-neutral.** Skills talk to `IIacGenerator`; Terraform and Bicep ship
  with parity so the template does not cloud-lock by shipping Bicep alone. A third
  backend (Pulumi, CloudFormation) is added by implementing `IIacGenerator` and
  registering it under a new keyed-DI key.
- **Sandboxed CLIs.** Every CLI run — `terraform`, `bicep`, `checkov`, `tfsec`,
  `arm-ttk` — executes inside the PR-3 sandbox with the egress allowlist scoped to
  the provider/module registries. The generator never spawns a process on the host.

## Skills

| Skill | Purpose | Tools |
|-------|---------|-------|
| `iac-authoring` | Scaffold a module, plan it, and security-scan it before proposing | `iac_generate`, `iac_plan`, `iac_scan` |

## Configuration

Bind under `AppConfig:AI:Iac`:

```json
{
  "AI": {
    "Iac": {
      "Enabled": true,
      "EnabledBackends": ["terraform", "bicep"],
      "TerraformVersion": "1.9.5",
      "BicepVersion": "0.30.23",
      "CheckovVersion": "3.2.0",
      "TfsecVersion": "1.28.11",
      "ArmTtkVersion": "0.24",
      "BlockingSeverity": "High",
      "RegistryAllowlist": [
        "registry.terraform.io",
        "releases.hashicorp.com",
        "mcr.microsoft.com"
      ]
    }
  }
}
```

When enabled, `IacStartupValidator` requires each enabled backend to carry a pinned
CLI version, a valid `BlockingSeverity`, and a non-empty `RegistryAllowlist`.
Misconfiguration fails loud at boot rather than shipping a half-working skill.
