# Presentation.FoundryHost

Packages the full agentic harness as an **Azure AI Foundry hosted agent** — a container Foundry
Agent Service starts, scales, and exposes over the OpenAI-compatible `/responses` protocol. Unlike a
Foundry *prompt agent* (where the platform owns the loop), a hosted agent runs *your* code, so the
harness's in-process pipeline — skills, governance, tool-output compression, prerequisite gating,
telemetry — survives intact.

The host reuses the same composition root every other host uses (`GetServices`), builds the agent
through `IAgentFactory` exactly as the desktop and web hosts do, and hands it to the Foundry hosting
library. **There is no new agent-type concept**: which agent this deployment exposes is selected by
id (`FOUNDRY_AGENT_ID`, default `default`), resolved against the same `AGENT.md` manifests the rest
of the harness discovers.

## Runtime persistence model

Foundry runs each conversation in its own **session** — a VM-isolated sandbox with a **persistent
`$HOME`**. Key facts (verified against Microsoft Learn, *What are hosted agents?*):

- `$HOME` (and the `/files` upload area) **survive idle/resume**: when a session is idle for 15
  minutes the compute is deprovisioned, but `$HOME` is preserved and **restored on resume**.
- A session — and its `$HOME` — is **permanently deleted after 30 days of inactivity**.
- Conversation history is stored **durably in Foundry**, independently of compute. The host sets
  the harness to *not* re-persist it, so Foundry remains the single source of truth.
- **Only `$HOME`/`/files` are guaranteed to persist** — *not* the container's app/publish
  directory. So the host re-roots every local-disk write under `$HOME` (see below).

**Net effect:** out of the box, every conversation gets durable, idle-surviving memory with **zero
external services**. External managed stores are needed *only* to share knowledge **across different
conversations or users**, or beyond the 30-day window.

## Environment variables

### Injected by Foundry at deploy time

| Variable | Purpose |
|---|---|
| `FOUNDRY_PROJECT_ENDPOINT` | Foundry project endpoint used for model inference. |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` | Model deployment the agent runs against. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry sink; presence auto-enables the Azure Monitor exporter. |
| `FOUNDRY_AGENT_ID` | (Optional) which discovered `AGENT.md` to expose. Defaults to `default`. |
| `HOME` | Persistent session storage root. The host roots all local state under `$HOME/agent-state`. |

### Optional — external stores for cross-conversation / cross-user persistence

Each subsystem promotes itself from its in-process default to an external managed service **only when
its connection variable is supplied**. Supply none and the container still boots (durable
per-conversation memory via `$HOME`); supply a variable and that subsystem goes external. A
*malformed* connection still fails loudly at startup. Add the ones you need to `agent.yaml`'s
`environment_variables`.

| Variable(s) | Promotes | Notes |
|---|---|---|
| `GRAPH_PROVIDER` (`neo4j`\|`postgresql`) **and** `GRAPH_CONNECTION_STRING` | Knowledge graph + cross-session memory; also enables GraphRAG retrieval | Both required. `GRAPH_PROVIDER` is validated (a value other than `neo4j`/`postgresql` fails at startup with a clear message rather than an opaque DI error). |
| `AZURE_SEARCH_ENDPOINT` | RAG vector + BM25 store → Azure AI Search | Without it, RAG uses the in-process FAISS/FTS5 store (per-session, rebuilt from ingested docs). |
| `AZURE_SEARCH_API_KEY` | — | Optional; omit to use the agent's managed identity (preferred). |
| `AZURE_SEARCH_INDEX` | — | Optional; defaults to the harness's configured index name. |
| `REDIS_ENDPOINT` | `IDistributedCache` → Redis | Shared cache across sessions/replicas. |
| `REDIS_SECRET` | — | Optional Redis password. |

Any of the above can also be set directly via the native `AppConfig__...` env key, which the host
never overwrites.

### Capability note: sandbox

The `Foundry` profile sets `Sandbox:Enabled=false`. Docker-in-Micro-VM is unavailable and the
process sandbox's Windows Job-Object limits don't apply on the Linux runtime, so sandboxed tool
execution is the one harness capability degraded under hosted agents. The closed-by-default model
means such tools are refused with a clear capability message rather than running unsandboxed.

## Deploy

```bash
azd provision   # Foundry project, model deployment, App Insights, container registry
azd deploy      # builds the Dockerfile, pushes to ACR, registers the hosted agent
```

The translation from these environment variables to harness config keys lives in
`FoundryHostBootstrap.BuildConfigOverrides` (pure and unit-tested in `Presentation.FoundryHost.Tests`).
