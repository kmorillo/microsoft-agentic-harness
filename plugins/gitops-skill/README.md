# GitOps Skill Pack

Drift detection, cluster-health probing, K8sGPT root-cause analysis, and
ChangeProposal-gated remediation for **Flux** and **Argo CD** clusters.

## Design principles

- **Read-only against the cluster.** No skill in this pack can run `kubectl apply`,
  `kubectl delete`, or `helm upgrade` — those tools are explicitly denied on every
  skill and the denial is bypass-immune.
- **Fixes flow through ChangeProposal.** The only state-changing tool,
  `propose_remediation`, submits a `ChangeProposal` against the configured Git
  repository and lets the gate pipeline + approver decide. The skill never writes
  to Git or the cluster directly.
- **Controller-neutral.** Skills talk to `IGitOpsController`; the active backend
  (Flux or Argo CD) is selected by `AppConfig.AI.GitOps.ActiveController`. Switching
  controllers is a config change, not a skill change.
- **K8sGPT is required, not degraded.** `gitops-cluster-debug` depends on a K8sGPT
  MCP server. A misconfigured deployment fails loud at boot via
  `GitOpsStartupValidator` rather than shipping a half-working skill.

## Skills

| Skill | Purpose | Tools |
|-------|---------|-------|
| `gitops-repo-audit` | Detect drift, read health, propose a fix as a ChangeProposal | `detect_drift`, `cluster_health`, `propose_remediation` |
| `gitops-cluster-debug` | Root-cause cluster problems without mutating | `k8sgpt_analyze`, `cluster_health` |
| `gitops-knowledge` | Retrieve past GitOps learnings and correlate with live analysis | `document_search`, `k8sgpt_analyze` |

## Configuration

Bind under `AppConfig:AI:GitOps`:

```json
{
  "AI": {
    "GitOps": {
      "Enabled": true,
      "ActiveController": "flux",
      "K8sGptMcpServerName": "k8sgpt",
      "FluxApiBaseUrl": "https://flux.example.com",
      "ArgoCdApiBaseUrl": "",
      "RemediationRepoUrl": "https://github.com/acme/cluster-config",
      "RemediationBranch": "main"
    }
  }
}
```

`K8sGptMcpServerName` must reference an entry under `AppConfig:AI:McpServers:Servers`.
Outbound calls to the Flux/Argo CD API and the Git host must be present in the egress
allowlist when the egress layer is enabled.
