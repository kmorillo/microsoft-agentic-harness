# Purview-Backed Data Classification (Real DLP) — PR Plan

**Status:** Greenlit-pending (Matt to approve). Scoping only — no code until approval.
**Decisions captured:** (1) Target **both** Purview worlds — Information Protection *and* Data Map. (2) Cover **all** tool/asset types — with an explicit Unknown-asset policy for what Purview cannot classify.

---

## Goal

Add classification-aware Data Loss Prevention to the agent: before a tool reads/writes an external
asset, resolve that asset's **Purview sensitivity label / classification**, evaluate it against a
policy, and **allow / redact / block** the call — with audit + OTel metrics, fail-closed.

This is fundamentally different from the existing `IResponseSanitizer` controls
(`ExfiltrationUrlDetector`, `CredentialRedactor`, …). Those scan *output text content* for dangerous
*patterns*. This asks *"is the asset I'm about to touch classified, per the org's Purview policy?"* —
**pre-execution access control**, not post-execution content scrubbing.

## Why it does NOT fit the existing sanitizer seam

`IResponseSanitizer.Sanitize(string content, string? toolName)` is **synchronous** and **content-only**.
A Purview check is **async** (network) and needs the **asset identity** (file path / blob URI / table
qualified name) — which lives in the tool's **input arguments**, not its output. Wrong shape. Do not
extend `IResponseSanitizer`.

## Integration point (confirmed in code)

`IToolInvocationGovernor` is the live, fail-closed, pre-execution tool path, gated by
`GovernanceConfig.EnforceToolInvocation` (`GovernanceConfig.cs:27`). The classification gate becomes
one more check the governor runs. This avoids the dead-`IToolRequest`-MediatR trap (the Loop
Engineering work found `ResponseSanitizationBehavior` never fires on the agent path because nothing
live implements `IToolRequest`).

---

## The two Purview worlds (both in scope)

| | Information Protection (MIP) | Data Map / Unified Catalog |
|---|---|---|
| Labels | Documents/emails/files, incl. **embedded** label on a file | Data-estate **assets** (blob, ADLS, Azure SQL columns) as metadata |
| API | Microsoft Graph `informationProtection` / `sensitivityLabel` (GA) + MIP file API for embedded labels | Data Map REST (Atlas-based) |
| Auth | Entra app reg, Graph scope `InformationProtectionPolicy.Read` | Purview account + managed identity, data-reader role |
| Caveats | `evaluateClassificationResults` computes a label from classification results *you supply* — it does **not** scan a file for you | Labels/classifications are **scan-time metadata** (stale between scans); DB-column labels are **preview** and source-dependent |

## The "all assets" reality (honest coverage matrix)

Purview is **not** omniscient. "All tools" is handled by a resolver + Unknown-policy, not by pretending
every asset is classifiable.

| Tool / asset | Resolvable label source | Result |
|---|---|---|
| Local file (`file_system`) | Embedded MIP label via MIP file API only | Often **Unknown** (most local files carry no embedded label) |
| Azure Blob / ADLS Gen2 | Data Map (if scanned) | Covered |
| Azure SQL table/column | Data Map (preview) | Partially covered |
| PostgreSQL / MySQL / Mongo / most others | — | **Unknown** (no Purview classification support) |
| Arbitrary MCP server output | — | **Unknown** (no asset identity Purview knows) |

**Design consequence:** an `IAssetReferenceResolver` per tool maps tool input → `AssetReference`,
routes to the right provider, and everything Purview can't classify falls to a configurable
**`UnknownAssetAction`** (Audit / Allow / Block). High-security consumers set Block-Unknown.

---

## Architecture (Clean Architecture, follows existing harness conventions)

### Domain.AI / Governance
- `AssetReference` (record): `AssetType` enum {LocalFile, AzureBlob, AdlsGen2, AzureSql, CosmosDb, Unknown}, `Identifier`, optional `TenantId`
- `SensitivityLabel` (Id, Name, Priority, protection flags)
- `DataClassification` (system/custom name, confidence)
- `AssetLabelResult` (label?, classifications, `LabelSource` {InformationProtection, DataMap, None}, `RetrievedAtUtc`, `IsStale`)
- `ClassificationPolicyDecision` (Allow / Redact / Block, matched rule, reason)
- `ClassificationEnforcementMode` enum {Off, Audit, Enforce}

### Application.AI.Common / Interfaces / Governance
- `IDataClassificationProvider` — `Task<AssetLabelResult> GetLabelAsync(AssetReference, CancellationToken)`
- `IAssetReferenceResolver` — keyed by tool; `bool TryResolve(toolName, toolInput, out AssetReference)`
- `IClassificationPolicyEvaluator` — `ClassificationPolicyDecision Evaluate(AssetLabelResult, ctx)`

### Config — `DataClassificationConfig` under `AppConfig:AI:Governance`
- `Mode` (Off/Audit/Enforce), `UnknownAssetAction`, `CacheTtl`
- `LabelActions` map (label name → Allow/Redact/Block)
- per-provider enable flags + auth (managed identity), Purview account endpoint, Graph tenant
- FluentValidation validator

### Infrastructure.AI.Governance
- `GraphSensitivityLabelClient : IDataClassificationProvider` (MIP / Graph world)
- `PurviewDataMapClient : IDataClassificationProvider` (Data Map world)
- `RoutingDataClassificationProvider` — picks provider by `AssetType`
- `CachedDataClassificationProvider` — TTL cache decorator (labels change on scan cadence, not per call; caching is mandatory for latency)
- `NotConfiguredDataClassificationProvider` — fail-fast default (matches harness `NotConfigured*` pattern); `NoOp` variant for `Mode=Off`

---

## PR breakdown (4 PRs, one concern each)

**PR1 — Domain + Application seam + policy (no network). ✅ BUILT (uncommitted, awaiting approval to PR).**
Domain models, **two** interfaces (`IDataClassificationProvider`, `IClassificationPolicyEvaluator`),
`DataClassificationConfig` + validator, `DefaultClassificationPolicyEvaluator` (pure),
`NotConfigured`/`NoOp` providers, DI wiring in both governance paths. 20 unit tests green; solution builds 0 errors.
- **Change vs plan:** `IAssetReferenceResolver` **deferred to PR4** — its signature depends on the
  governor's tool-argument shape (unverified), so defining it now risks a throwaway contract.
- **Architecture note:** the two shared enums (`ClassificationAction`, `ClassificationEnforcementMode`)
  live in **Domain.Common** (not Domain.AI) to satisfy dependency direction — same as `ThreatLevel`.
- **Robustness note:** `DefaultClassificationPolicyEvaluator` does explicit case-insensitive label
  matching rather than trusting the dictionary comparer (IConfiguration binding drops it).

**PR2 — Graph / MIP provider (Information Protection world).**
`GraphSensitivityLabelClient` (list label defs + resolve a file's embedded label), Entra auth via managed
identity, `CachedDataClassificationProvider`, DI + config wiring. Tests against mocked Graph HTTP.

**PR3 — Purview Data Map provider (data-estate world).**
`PurviewDataMapClient` (query asset by qualified name → classifications + labels), `RoutingDataClassificationProvider`
to select MIP vs Data Map by `AssetType`, managed-identity auth. Tests mocked.

**PR4 — Asset resolvers + governor enforcement (the live path).**
`IAssetReferenceResolver` impls keyed by tool (file_system → LocalFile; cloud-data tools → blob/sql/adls;
default → Unknown). Integrate the gate into `IToolInvocationGovernor`: resolve → GetLabel → Evaluate →
allow/redact/block + audit + OTel metric, honoring `Mode` (Audit observes, Enforce blocks). Integration
tests proving block/audit/allow on the live governor path.

---

## Risks / caveats (must stay visible to consumers)

1. **Coverage is patchy + partly preview.** DB-column labels: Azure SQL yes (preview); Postgres/MySQL/Mongo no. Document in config + README.
2. **Labels are stale by design.** Data Map tags reflect the last scan, not live state. `IsStale` surfaced on `AssetLabelResult`.
3. **Value depends on the consumer's Purview deployment.** Only useful if they run Purview with labels populated. Template ships the seam + reference clients + fail-safe default (consumer wires their tenant).
4. **Latency.** Each gated tool call → a Graph/Purview round trip. Caching mandatory; default `Mode=Audit` so it observes before it blocks.
5. **Unknown is the common case for local files + MCP.** `UnknownAssetAction` must be a deliberate, documented choice.

## Open decisions for Matt
- Default `UnknownAssetAction` for the template (lean **Audit** — observe, don't break consumers).
- Default `Mode` (lean **Off**, opt-in like `EnforceToolInvocation`).
- Whether PR2/PR3 order should flip (which world first) — currently MIP first (broadest, file-oriented).
