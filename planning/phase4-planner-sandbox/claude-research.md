# Phase 4 Research: Planner & Code Sandbox

## Part A: Codebase Analysis

### 1. Existing Workflow & Orchestration

**MultiAgentWorkflow** (`Application.Core/Workflows/Orchestration/MultiAgentWorkflow.cs`)
- Static factory using MAF `WorkflowBuilder` with fan-out/fan-in barrier
- `AddFanOutEdge()` and `AddFanInBarrierEdge()` for parallel agent execution
- Results aggregated through `AggregateResultsExecutor`
- No DAG tracking — workflow is static, not dynamic

**RunOrchestratedTaskCommandHandler** (`Application.Core/CQRS/Agents/RunOrchestratedTask/`)
- CQRS handler: Phase 1 decomposes via orchestrator, Phase 2 delegates to sub-agents, Phase 3 synthesizes
- Scoped DI per sub-agent via `IServiceScopeFactory` for context isolation
- Subtask parsing from LLM response, turn tracking with `maxTotalTurns`
- Progress reporting, tool invocation tracking, metrics via `OrchestrationMetrics`
- **No DAG** — orchestrator decides sequentially at runtime

**AgentExecutorFactory** (`Application.Core/Workflows/Orchestration/AgentExecutorFactory.cs`)
- Wraps `IChatClient.GetResponseAsync()` as MAF executor via `.BindAsExecutor<TInput, TOutput>()`
- Exception handling converts failures to failed `AgentStepResult`
- No tool execution at this layer (raw chat response only)

### 2. Tool Execution & Sandboxing

**IToolExecutionStrategy / BatchedToolExecutionStrategy**
- Interface: `ExecuteBatchAsync(requests, progress, ct)`
- Implementation classifies tools via `IToolConcurrencyClassifier`
- Read-only tools execute in parallel (bounded by `StreamingExecutionConfig.ParallelBatchSize`)
- Write-serial tools execute sequentially in request order
- Results returned in original request order; all exceptions caught -> failed `ToolResult`

**IToolConcurrencyClassifier / ToolConcurrencyClassifier**
- Classifies by `ITool.IsReadOnly` and `ITool.IsConcurrencySafe`
- ReadOnly vs WriteSerial classification

**IFileSystemService / FileSystemService**
- Allowlist by base paths (normalized, case-insensitive)
- 10 MB file size limit on read/write
- System directory blocklist via `BuildSystemBlocklist()`
- Search skip dirs: `.git`, `.svn`, `bin`, `obj`, `node_modules`, `packages`, `logs`
- Max 100 search results, max 1,000 files scanned
- Symlink resolution via `Path.GetFullPath()`
- DI: paths from `appConfig.Infrastructure.FileSystem.AllowedBasePaths`

### 3. Phase 1-3 Integration Points

**Phase 1: Autonomy Tiers**
- `IAutonomyTierResolver` — two overloads: `Resolve(SubagentType)` and `Resolve(SubagentDefinition)`
- `AutonomyLevel` enum in `Domain.AI/Governance/`
- `DefaultAutonomyTierResolver` in `Infrastructure.AI/Governance/`

**Phase 2: Escalation**
- `IEscalationService` — blocking (`RequestEscalationAsync`) and non-blocking (`QueueEscalationAsync`) modes
- Approval strategies: `AnyOf`, `AllOf`, `Quorum` — registered as keyed singletons
- `DefaultEscalationService` in `Infrastructure.AI/Escalation/`

**Phase 3: Drift Detection**
- `IDriftDetectionService` — `EvaluateDriftAsync`, `GetBaselineAsync`, `UpdateBaselineAsync`, `GetDriftHistoryAsync`
- Related: `IDriftAuditStore`, `IDriftBaselineStore`, `IDriftNotifier`, `IDriftScorer`
- Domain models: `DriftScope`, `DriftScore`, `DriftSeverity`, `DriftEvent`, `DriftResolution`
- Implementations: `JsonlDriftAuditStore`, `InMemoryDriftBaselineStore`, `GraphDriftBaselineStore`

**Phase 3: Learnings**
- CQRS commands: `RememberCommand`, `RecallQuery`, `ImproveLearningCommand`, `ForgetCommand`, `RecordLearningAccessCommand`
- Interfaces: `ILearningsStore`, `ILearningDecayService`, `ILearningsDriftBridge`, `ILearningNotificationChannel`

### 4. AG-UI / Real-Time Event Infrastructure

**SignalR Hub** (`Presentation.AgentHub/Hubs/AgentTelemetryHub.cs`)
- WebSocket upgrade, bearer token auth via Azure AD
- Conversation groups: `conversation:{conversationId}`
- Events: `TokenReceived`, `TurnComplete`, `ToolCallStarted`, `ToolCallCompleted`, `SpanReceived`, `Error`, `HistoryTruncated`
- `ConversationLockRegistry` — one `SemaphoreSlim` per conversation

**AG-UI SSE** (`Presentation.AgentHub/AgUi/`)
- `AgUiEventWriter` — SSE with JSON polymorphic frames, `data: {json}\n\n` format
- `AgUiEvents` — polymorphic base `AgUiEvent` with `JsonPolymorphicAttribute`
- `AgUiEndpoints` — HTTP endpoint returning `IAsyncEnumerable<AgUiEvent>` as `text/event-stream`

### 5. DI Registration Patterns

**Keyed DI**: Approval strategies, workflows (`rag-pipeline`, `kg-ingestion`, `governance-approval`, `optimization-iteration`)

**Layer Registration Order**:
1. `AddApplicationCommonDependencies` — exceptions, logging, caching
2. `AddApplicationAIDependencies` — AI interfaces
3. `AddApplicationCoreDependencies` — CQRS, validators, workflows
4. `AddInfrastructureAIDependencies` — chat clients, file system, stores
5. `AddInfrastructureCommonDependencies` — HTTP, database, secrets

**MediatR Pipeline Behaviors** (in order):
- `UnhandledExceptionBehavior` -> `AuditTrailBehavior` -> `AgentContextPropagationBehavior` -> `RequestValidationBehavior` -> `AuthorizationBehavior` -> `RequestTracingBehavior` -> `CachingBehavior`

### 6. Testing Setup

- Frameworks: xUnit, Moq, FluentAssertions
- Test projects: `Application.Core.Tests`, `Application.AI.Common.Tests`, `Infrastructure.AI.Tests`, `Domain.AI.Tests`, `Presentation.AgentHub.Tests`
- Pattern: Arrange-Act-Assert with mock setup in constructor
- Integration: `TestWebApplicationFactory` in AgentHub.Tests
- Helpers: `TestableAIAgent` for predefined responses

---

## Part B: Web Research — Best Practices 2025

### 1. DAG Workflow Orchestration in .NET

**Option A: MAF WorkflowBuilder (RECOMMENDED)**
- MAF ships workflow programming model with typed `Executor` steps (`TInput -> TOutput`)
- Wire into directed graph via `WorkflowBuilder`
- `Microsoft.Agents.AI.DurableTask` package for checkpoint/resume — stateful execution surviving restarts, automatic checkpointing, distributed execution
- Already used in our codebase for fan-out/fan-in

**Option B: Custom Kahn's Algorithm DAG**
- For node types MAF doesn't cover (conditional branches, human gates)
- Layer-based parallel execution: topological sort groups nodes into layers, `Task.WhenAll` within layers
- V3 pattern: state machine in database, polls for nodes where all parent deps are "completed"

**Option C: Elsa Workflows 3** — heavyweight, own runtime/persistence/UI. Better for no-code scenarios.

**Option D: Durable Task SDK (self-hosted)** — battle-tested fan-out/fan-in, lower-level than MAF.

**Recommendation**: Use MAF WorkflowBuilder as foundation, extend with custom node types (LLM call, tool use, human gate, conditional branch). Wrap MAF executors with `IPlanStep` abstraction in Domain layer. Fall back to Kahn's for DAG validation and parallel layer computation.

| Approach | Pros | Cons |
|----------|------|------|
| MAF WorkflowBuilder | Native to stack, typed, durable | Still in preview, API may shift |
| Custom Kahn's DAG | Full control, minimal deps | Own persistence, retry, observability |
| Elsa 3 | Feature-complete, visual designer | Heavy dependency, opinionated runtime |
| Durable Task SDK | Battle-tested, self-hosted | Lower-level, more boilerplate |

Sources: [MAF Durable Workflows](https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/), [SK Planners Deprecated](https://learn.microsoft.com/en-us/semantic-kernel/concepts/planning), [FlowForge DAG Engine](https://dev.to/ganesh_parella/building-flowforge-architecting-a-dag-based-workflow-engine-in-net-5ad4), [Durable Functions Fan-Out](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-fan-in-fan-out)

### 2. Process Isolation & Sandboxed Execution in .NET

**Isolation Spectrum**:
| Level | Technology | Startup | Isolation |
|-------|-----------|---------|-----------|
| 0 | Raw Process.Start | Instant | None |
| 1 | Docker/LXC | ~100ms | Namespace + cgroup |
| 2 | seccomp-BPF + Docker | ~100ms | Syscall filtering |
| 3 | gVisor | ~200ms | User-space kernel |
| 4 | Firecracker microVMs | ~125ms | Full VM boundary |

**Windows: Job Objects** — primary mechanism for subprocess resource control:
- `JOB_OBJECT_LIMIT_PROCESS_TIME` — per-process CPU time limit; system terminates when exceeded
- `JOB_OBJECT_LIMIT_JOB_MEMORY` / `JOB_OBJECT_LIMIT_PROCESS_MEMORY` — memory caps
- `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` — hard kill guarantee on handle close
- `JOB_OBJECT_LIMIT_ACTIVE_PROCESS` — limits simultaneous process count
- Requires P/Invoke from .NET

**Linux: cgroups v2** — equivalent resource limits. .NET respects container limits for GC heap sizing.

**Docker.DotNet SDK** — official .NET client for Docker API. Async, supports resource limits programmatically.

**Roslyn Scripting Sandbox: AVOID** — .NET Core removed AppDomains and CAS. No in-process sandboxing. Must use process-level isolation.

**Recommendation: Two-tier approach**:
1. **Default**: `System.Diagnostics.Process` + Windows Job Objects (fast, low overhead, trusted tools)
2. **Elevated**: Docker.DotNet for untrusted/LLM-generated code (`--network=none`, memory/CPU caps, auto-remove)

Hard kill: `CancellationTokenSource` with timeout + `Process.Kill(entireProcessTree: true)` backstop. Job Objects provide OS-level guarantee.

Sources: [Agent Sandboxing 2025](https://tianpan.co/blog/2026-03-09-agent-sandboxing-secure-code-execution), [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects), [Docker.DotNet](https://github.com/dotnet/Docker.DotNet), [Deno Security](https://docs.deno.com/runtime/fundamentals/security/)

### 3. AG-UI SSE Event Patterns

**AG-UI Protocol — 17 Event Types**:
- Lifecycle: `RUN_STARTED`, `RUN_FINISHED`, `RUN_ERROR`, `STEP_STARTED`, `STEP_FINISHED`
- Text: `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`, `TEXT_MESSAGE_END`
- Tool: `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_END`, `TOOL_CALL_RESULT`
- State: `STATE_SNAPSHOT`, `STATE_DELTA` (RFC 6902 JSON Patch), `MESSAGES_SNAPSHOT`
- Special: `CUSTOM`, `RAW`

**MAF AG-UI Integration**: `MapAGUI` extension method handles SSE streaming and request routing.

**.NET 10 SSE**: First-class support via `TypedResults.ServerSentEvents<T>()` with `IAsyncEnumerable<SseItem<T>>`. Built-in `Last-Event-ID` for replay on reconnect.

**Recommendation — Map plan steps to AG-UI events**:
- Plan start -> `RUN_STARTED`
- Step begin -> `STEP_STARTED`
- LLM streaming -> `TEXT_MESSAGE_*`
- Tool execution -> `TOOL_CALL_*`
- State changes -> `STATE_DELTA` (JSON Patch with step status)
- Step end -> `STEP_FINISHED`
- Plan end -> `RUN_FINISHED` / `RUN_ERROR`

Use `STATE_SNAPSHOT` at plan start (full graph), then `STATE_DELTA` for each transition.

Sources: [AG-UI Protocol](https://docs.ag-ui.com/), [AG-UI Event Types](https://www.copilotkit.ai/blog/master-the-17-ag-ui-event-types-for-building-agents-the-right-way), [MAF AG-UI Integration](https://learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/), [.NET 10 SSE](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10)

### 4. Capability-Based Security for Tool Permissions

**WASI Capability Model** (reference architecture):
- Access rights are unforgeable handles, not string ACLs
- Programs receive only needed capabilities (Principle of Least Privilege)
- No global access control — capabilities passed directly

**Deno Permissions Model** (practical reference):
- Per-resource granularity: `--allow-read[=PATH]`, `--allow-net[=HOST:PORT]`, `--allow-run[=PROGRAM]`, `--allow-env[=VAR]`
- Deny flags override allow flags
- No `--allow-all` in production

**Execution Attestation**:
- in-toto framework: DSSE envelope wrapping subject (artifact + SHA-256 digest) + predicate (provenance)
- .NET: `HMACSHA256` for signing tool outputs
- Pattern: hash inputs + outputs, sign attestation with per-session key, store chain alongside logs

**Recommendation — Hybrid approach**:
1. `[Flags] ToolCapability` enum: `FileRead`, `FileWrite`, `NetworkAccess`, `Subprocess`, `EnvRead`, `DatabaseRead`, `DatabaseWrite`, `LlmInvocation`
2. `ToolPermissionProfile` record: required capabilities + scoped allowlists (paths, hosts, programs) + deny lists
3. `ToolCapabilityBehavior<TRequest, TResponse>` MediatR pipeline behavior: fast reject on missing capabilities, fine-grained scope validation
4. HMAC-SHA256 attestation for tool output integrity

Sources: [WASI Capabilities](https://marcokuoni.ch/blog/15_capabilities_based_security/), [Deno Security](https://docs.deno.com/runtime/fundamentals/security/), [in-toto Attestation](https://github.com/in-toto/attestation), [HMACSHA256 .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hmacsha256)

---

## Cross-Cutting: Clean Architecture Mapping

| Topic | Domain | Application | Infrastructure | Presentation |
|-------|--------|-------------|----------------|-------------|
| DAG Planner | `PlanStep`, `PlanGraph`, `StepType` enum | `IWorkflowOrchestrator`, `IPlanExecutor`, MediatR commands | MAF WorkflowBuilder adapters, Durable Task | AG-UI SSE endpoints |
| Sandbox | `ToolCapability` enum, `ToolPermissionProfile` | `ISandboxExecutor`, `IResourceLimiter` | Process+JobObject, Docker.DotNet | Sandbox config in appsettings |
| AG-UI Events | Event type constants | `IPlanProgressEmitter` interface | SSE channel/stream impl | `MapAGUI` endpoint, SignalR bridge |
| Capabilities | Permission matrix types | `ToolCapabilityBehavior` (MediatR) | Capability enforcement, attestation signing | Admin UI for permission config |
