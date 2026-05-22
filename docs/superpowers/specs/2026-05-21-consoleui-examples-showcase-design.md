# ConsoleUI Examples Showcase — Design Spec

**Date:** 2026-05-21
**Status:** Approved
**Scope:** Add 11 new examples to `Presentation.ConsoleUI` covering all major harness subsystems, restructure the interactive menu into subsystem-based groups.

---

## Goals

1. Every major harness subsystem has a runnable, self-contained ConsoleUI example
2. Template consumers can discover capabilities by browsing the menu
3. Examples work offline (in-memory backends) by default, with opt-in live mode
4. Tutorial-style output: brief concept explanation + numbered steps + structured results

## Non-Goals

- End-to-end integration testing (that belongs in the test projects)
- Production-ready configurations (examples use synthetic data)
- UI framework beyond Spectre.Console

---

## Menu Restructuring

Replace the current 4-group menu in `App.cs` with 8 subsystem-based groups:

```
Agents
  ├── Research Agent (Standalone)          [existing]
  ├── Orchestrator Agent (Multi-Agent)     [existing]
  ├── Persistent Agent (AI Foundry)        [existing]
  └── A2A Agent-to-Agent                   [existing]

RAG & Retrieval
  ├── RAG Pipeline Demo                    [existing]
  └── Multi-Source Retrieval               [NEW]

Knowledge Graph
  ├── Knowledge Graph Memory               [NEW]
  └── Knowledge Graph Compliance           [NEW]

Governance & Safety
  ├── Response Sanitization                [NEW]
  ├── Escalation & Approvals               [NEW]
  └── Pipeline Behaviors                   [NEW]

Skills & Tools
  ├── Skills Discovery & Budget            [NEW]
  ├── Tool Converter Demo                  [existing]
  ├── MCP Tools Discovery                  [existing]
  └── Sandbox Capabilities                 [NEW]

Observability
  ├── Drift Detection                      [NEW]
  ├── Learnings Log                        [NEW]
  └── Budget & Health Tracking             [NEW]

Optimization
  └── Meta-Harness Optimizer               [existing]

Setup
  ├── Setup User Secrets                   [existing]
  └── Show Configuration                   [existing]
```

**20 total items** (9 existing + 11 new). Each group has 1-4 items.

---

## Example Conventions

Every new example follows these patterns:

### Structure
- **File:** `Examples/{ExampleName}Example.cs`
- **Class:** `{ExampleName}Example` with constructor DI + `ILogger<T>`
- **Entry point:** `public async Task RunAsync()`
- **Registration:** Transient in `Program.cs`, injected into `App.cs`

### Runtime Behavior
1. **Mode detection:** Inject `IOptionsMonitor<AppConfig>` and check for non-empty connection strings / endpoints for the relevant subsystem. If absent, use in-memory/keyed DI alternatives. Detection is per-example, not global — e.g., KG examples check graph backend config, multi-source checks vector store config
2. **Tutorial header:** 2-3 line `ConsoleHelper.DisplayInfo()` explaining what the example demonstrates
3. **Mode badge:** `ConsoleHelper.DisplayModeInfo(bool isLive)` shows `[LIVE]` or `[OFFLINE]`
4. **Numbered steps:** `ConsoleHelper.DisplayStep(current, total, description)` for each phase
5. **Result display:** Spectre tables for structured output, markup for inline results
6. **Error resilience:** Catch and display errors gracefully, never crash the menu loop

### ConsoleHelper Additions
Add to `Common/Helpers/ConsoleHelper.cs`:
- `DisplayStep(int current, int total, string description)` — renders `[Step N/M] description`
- `DisplayModeInfo(bool isLive)` — renders `[LIVE] Connected to...` or `[OFFLINE] Using in-memory backends`

---

## New Examples — Detailed Design

### 1. KnowledgeGraphMemoryExample (~200 lines)

**Purpose:** Demonstrate the Remember/Recall/Forget/Improve lifecycle, session cache, and feedback-weighted learning.

**DI Services:**
- `IKnowledgeMemory`
- `ISessionKnowledgeCache`
- `IFeedbackStore`
- `IFeedbackDetector`
- `ILogger<KnowledgeGraphMemoryExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Remember 3-4 facts (Person, Technology, Concept entity types) | `RememberAsync(key, content, entityType)` |
| 2/6 | Recall by query — display ranked results with scores in a Spectre table | `RecallAsync(query, maxResults)` |
| 3/6 | Improve — simulate user feedback, show weight adjustment | `ImproveAsync(userMessage, assistantResponse, relevantNodeIds)` |
| 4/6 | Session cache — add nodes, search, display count, flush to graph | `ISessionKnowledgeCache.Add/Search/FlushToGraphAsync` |
| 5/6 | Forget — remove a fact, verify recall returns nothing | `ForgetAsync(key)` |
| 6/6 | (Live mode) Feedback detector analyzes conversation turn | `IFeedbackDetector.DetectFeedbackAsync` |

**Offline mode:** Uses `InMemoryGraphStore` backend. Skips step 6 (requires LLM for feedback detection).

---

### 2. KnowledgeGraphComplianceExample (~180 lines)

**Purpose:** Show provenance stamping → tenant isolation → GDPR erasure as a connected compliance flow.

**DI Services:**
- `IProvenanceStamper`
- `IKnowledgeScopeValidator`
- `IErasureOrchestrator`
- `IMemoryAuditSink` (keyed: `"structured_logging"`)
- `IRetentionPolicyProvider`
- `ILogger<KnowledgeGraphComplianceExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Create provenance stamps for different pipelines/tasks | `CreateStamp(sourcePipeline, sourceTask)` |
| 2/6 | Stamp nodes and edges — display metadata in table | `StampNode/StampEdge` |
| 3/6 | Set up two tenant scopes, validate cross-tenant access blocked | `ValidateAccess(scope, targetTenantId)` |
| 4/6 | Run erasure by owner — show ErasureReceipt | `EraseByOwnerAsync(ownerId)` |
| 5/6 | Emit audit events to sink | `IMemoryAuditSink.EmitAsync` |
| 6/6 | Display retention policies per entity type | `GetAllPolicies()` |

**Offline mode:** All operations work against in-memory stores.

---

### 3. GovernanceSanitizationExample (~160 lines)

**Purpose:** Demonstrate multi-layer response sanitization catching credentials, prompt injection, and exfiltration URLs.

**DI Services:**
- `ICompositeResponseSanitizer`
- `IEnumerable<IResponseSanitizer>` (to show individual sanitizers)
- `ISecretRedactor`
- `ILogger<GovernanceSanitizationExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Run composite sanitizer on clean text — "no findings" | Baseline behavior |
| 2/6 | Inject AWS key (`AKIA...`) into response — show credential redaction | `CredentialRedactor` catching secret patterns |
| 3/6 | Inject prompt injection payload (system override tags) | `ResponseInjectionScrubber` catching injection |
| 4/6 | Inject exfiltration URL (`https://evil.requestbin.com/...`) | `ExfiltrationUrlDetector` catching data leak |
| 5/6 | Run all three combined — show aggregated result | `ICompositeResponseSanitizer.Sanitize` with all findings |
| 6/6 | Demo `ISecretRedactor` on config keys | `Redact()` and `IsSecretKey()` |

**Offline mode:** Fully offline — sanitizers are pure string processing, no external deps.

---

### 4. EscalationApprovalsExample (~180 lines)

**Purpose:** Demonstrate multi-approval workflows with AnyOf, AllOf, and Quorum strategies.

**DI Services:**
- `IEscalationService`
- `ILogger<EscalationApprovalsExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/7 | Create an escalation request ("Agent wants to delete production data") | `EscalationRequest` construction |
| 2/7 | Queue the request — show pending state | `QueueEscalationAsync` |
| 3/7 | AnyOf strategy: submit one approval → resolved | Single-approver workflow |
| 4/7 | AllOf strategy: partial approval → still pending, all → resolved | Unanimous requirement |
| 5/7 | Quorum (2-of-3): submit 2 approvals → quorum met | Threshold-based approval |
| 6/7 | Cancel an escalation — show cancellation outcome | `CancelEscalationAsync` |
| 7/7 | List pending escalations for an approver | `GetPendingEscalationsAsync` |

**Offline mode:** Fully offline — escalation service uses in-memory state.

---

### 5. SkillsDiscoveryExample (~150 lines)

**Purpose:** Show skills progressive disclosure (3-tier loading) and context budget management.

**DI Services:**
- `IContextBudgetTracker`
- `IServiceProvider` (to resolve skill definitions from built-in skills)
- `ILogger<SkillsDiscoveryExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Load built-in skill definitions (research-agent, orchestrator-agent, echo-test) | Skill discovery from filesystem |
| 2/6 | Show Tier 1 view — index card metadata (name, description, tags, token estimate) | Progressive disclosure level 1 |
| 3/6 | Show Tier 2 view — full instructions and tool declarations | Progressive disclosure level 2 |
| 4/6 | Context budget tracker — allocate tokens for system prompt, skills, tools, history | `RecordAllocation` per component |
| 5/6 | Show budget breakdown and remaining capacity in table | `GetBreakdown` and `GetRemainingBudget` |
| 6/6 | Trigger budget exceeded — show BudgetAssessment | `AssessContinuation` returning over-budget |

**Offline mode:** Fully offline — reads SKILL.md files from disk, budget tracking is in-memory.

---

### 6. DriftDetectionExample (~170 lines)

**Purpose:** Show EWMA-based quality monitoring with severity classification and escalation bridge.

**DI Services:**
- `IDriftDetectionService`
- `IDriftBaselineStore`
- `IDriftAuditStore`
- `ILogger<DriftDetectionExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Establish a baseline for an agent (quality score = 0.85) | Baseline configuration |
| 2/6 | Record a series of normal quality scores (0.80-0.90) | EWMA staying within bounds |
| 3/6 | Record degrading scores (0.70, 0.65, 0.60) — show EWMA trending down | EWMA drift calculation |
| 4/6 | Trigger LOW severity drift — display alert | Severity classification |
| 5/6 | Trigger HIGH severity drift — show escalation bridge activating | `DriftEscalationBridge` creating escalation |
| 6/6 | Show drift audit trail in table | Audit persistence |

**Offline mode:** Fully offline — EWMA is pure math, stores are in-memory.

---

### 7. LearningsLogExample (~150 lines)

**Purpose:** Demonstrate CQRS-based knowledge capture, search, decay tiers, and drift integration.

**DI Services:**
- `ILearningsStore` (keyed: `"in_memory"`)
- `ILearningsDriftBridge`
- `ILogger<LearningsLogExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Save 3-4 learning entries (categories: Bug Fix, Performance, Architecture) | `SaveAsync(LearningEntry)` |
| 2/6 | Search by criteria — show filtered results in table | `SearchAsync(LearningSearchCriteria)` |
| 3/6 | Update a learning — show version/timestamp change | `UpdateAsync` |
| 4/6 | Soft-delete a learning with reason | `SoftDeleteAsync(id, reason)` |
| 5/6 | Show drift bridge integration — learning feeds into drift detection | `ILearningsDriftBridge` |
| 6/6 | Display decay tier configuration (CRITICAL/STANDARD/EPHEMERAL) | Configured decay rates |

**Offline mode:** Uses `InMemoryLearningsStore`. Fully offline.

---

### 8. ObservabilityBudgetExample (~140 lines)

**Purpose:** Show budget tracking state machine, session health scoring, and agent config reporting.

**DI Services:**
- `IBudgetTrackingService`
- `ISessionHealthTracker`
- `IAgentConfigReporter`
- `ILogger<ObservabilityBudgetExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Register an agent with config reporter (model, temperature, tool/skill counts) | `RegisterAgent` |
| 2/6 | Record successful operations — show health = GREEN | `RecordSuccess` → score 2 |
| 3/6 | Record errors — show health degrading YELLOW → RED | `RecordError` → score 1 → 0 |
| 4/6 | Record spend amounts — show budget: clear → warning → critical | `RecordSpend` state transitions |
| 5/6 | Display thresholds and current spend in table | `GetThreshold` and `GetCurrentSpend` |
| 6/6 | Reset and show recovery to healthy state | Full recovery cycle |

**Offline mode:** Fully offline — all metrics are in-memory gauges/counters.

---

### 9. MultiSourceRetrievalExample (~180 lines)

**Purpose:** Show parallel multi-source retrieval orchestration with cost tracking.

**DI Services:**
- `IMultiSourceOrchestrator`
- `IRetrievalCostTracker`
- `ILogger<MultiSourceRetrievalExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Display configured retrieval sources (vector, graph; live adds web_search) | Source enumeration |
| 2/6 | Run a simple query through orchestrator — show parallel retrieval | `RetrieveFromAllSourcesAsync` |
| 3/6 | Display results grouped by source with relevance scores in table | Source attribution |
| 4/6 | Show cost tracker summary — tokens, latency per source | `GetSummary()` |
| 5/6 | Run a complex query — show different routing/behavior | `QueryComplexity.Complex` |
| 6/6 | Reset tracker, show cost comparison (simple vs complex) | Cost awareness |

**Offline mode:** Uses in-memory vector store + in-memory graph. Skips web search source.

---

### 10. SandboxCapabilitiesExample (~150 lines)

**Purpose:** Demonstrate capability-based tool permission enforcement.

**DI Services:**
- `ICapabilityEnforcer`
- `ToolPermissionProfileResolver`
- `ILogger<SandboxCapabilitiesExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Register tool types with capability attributes | `RegisterToolType` |
| 2/6 | Resolve permission profiles — show granted capabilities per tool | `Resolve(toolName)` |
| 3/6 | Enforce a valid request (file read on a file tool) — success | `EnforceAsync` passing |
| 4/6 | Enforce invalid request (network on read-only tool) — denial | `EnforceAsync` failing |
| 5/6 | Show deny-overrides-allow semantics | Runtime config override |
| 6/6 | Display full capability taxonomy (FileRead, FileWrite, Network, ProcessExec, etc.) | `ToolCapability` enum |

**Offline mode:** Fully offline — enforcement is pure logic.

---

### 11. PipelineBehaviorsExample (~200 lines)

**Purpose:** Visualize a MediatR request flowing through the full behavior pipeline.

**DI Services:**
- `ISender`
- `ILogger<PipelineBehaviorsExample>`

**Steps:**

| Step | Action | Shows |
|------|--------|-------|
| 1/6 | Send a simple command — show it passing through all behaviors (from logs) | Full pipeline trace |
| 2/6 | Trigger validation failure — show RequestValidationBehavior blocking | Validation pipeline |
| 3/6 | Trigger content safety flag — show ContentSafetyBehavior blocking | Safety guardrails |
| 4/6 | Trigger tool permission denial — show 3-phase resolution | Permission model |
| 5/6 | Show response sanitization on output | Post-execution scrubbing |
| 6/6 | Display full pipeline execution order with timing table | Pipeline architecture |

**Offline mode:** Behaviors that need LLM (content safety) show simulated results with explanation. Core behaviors (validation, permissions, sanitization) are fully offline.

---

## Files Changed

### New Files (11 examples)
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/KnowledgeGraphMemoryExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/KnowledgeGraphComplianceExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/GovernanceSanitizationExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/EscalationApprovalsExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/SkillsDiscoveryExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/DriftDetectionExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/LearningsLogExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/ObservabilityBudgetExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/MultiSourceRetrievalExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/SandboxCapabilitiesExample.cs`
- `src/Content/Presentation/Presentation.ConsoleUI/Examples/PipelineBehaviorsExample.cs`

### Modified Files
- `src/Content/Presentation/Presentation.ConsoleUI/App.cs` — New constructor params, menu restructuring
- `src/Content/Presentation/Presentation.ConsoleUI/Program.cs` — Register 11 new example classes
- `src/Content/Presentation/Presentation.ConsoleUI/Common/Helpers/ConsoleHelper.cs` — Add `DisplayStep` and `DisplayModeInfo`

### Estimated Effort
- ~1,860 lines of new example code
- ~100 lines of App.cs/Program.cs changes
- ~30 lines of ConsoleHelper additions
- **Total: ~1,990 lines across 14 files**

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Some interfaces may not have in-memory implementations registered | Verify DI registration in each layer's `DependencyInjection.cs` before implementing; add missing registrations |
| Keyed DI services may need specific config to resolve | Check `appsettings.json` defaults; ensure offline-friendly keys are available |
| LLM-dependent examples fail without API keys | Skip LLM steps in offline mode with clear explanation |
| Menu becomes too long to scan | 8 groups with max 4 items each keeps it navigable |
| Some domain models may need synthetic constructors | Use domain factory methods where available; create test-friendly builders only if necessary |
