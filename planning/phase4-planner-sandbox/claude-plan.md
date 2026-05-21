# Phase 4 Implementation Plan: Planner & Code Sandbox

## Overview

This plan adds two subsystems to the Microsoft Agentic Harness: a **DAG-based workflow planner** that extends the existing orchestration handler with formal graph representation, dependency-driven parallel execution, and persistent state; and a **two-tier code sandbox** with capability-based security, process isolation with resource limits, optional container isolation, and HMAC-signed execution attestation.

The harness is a .NET 10 Clean Architecture project with CQRS/MediatR, keyed DI, FluentValidation, and Phases 1-3 already merged (autonomy tiers, escalation, drift detection, learnings).

---

## Architecture Overview

### Layer Mapping

```
src/Content/
  Domain/Domain.AI/
    Planner/           # PlanGraph, PlanStep, PlanEdge, StepType, StepConfiguration (abstract),
                       #   PlanExecutionState, RetryPolicy, PlanConfiguration
    Sandbox/           # ToolCapability, ToolPermissionProfile, SandboxIsolationLevel,
                       #   ResourceLimits, ToolCapabilityAttribute
    Attestation/       # ToolExecutionAttestation

  Application/Application.AI.Common/
    Interfaces/
      Planner/         # IPlanExecutor, IPlanStepExecutor, IPlanValidator, IPlanStateStore,
                       #   IPlanGenerator, IPlanProgressNotifier
      Sandbox/         # ISandboxExecutor, IProcessResourceLimiter, ICapabilityEnforcer
      Attestation/     # IAttestationService, IAttestationStore

  Application/Application.Core/
    CQRS/
      Planner/         # ExecutePlanCommand, CreatePlanCommand, GeneratePlanCommand,
                       #   GetPlanQuery, GetPlanHistoryQuery, ListPlansQuery, etc.
      Sandbox/         # ExecuteInSandboxCommand

  Infrastructure/Infrastructure.AI/
    Planner/           # PlanExecutor, step executors (keyed DI), EfCorePlanStateStore,
                       #   LlmPlanGeneratorService
    Sandbox/           # ProcessSandboxExecutor, DockerSandboxExecutor, WindowsJobObjectManager
    Attestation/       # HmacAttestationService, EfCoreAttestationStore
    Persistence/       # PlannerDbContext, entity configs, migrations

  Presentation/Presentation.AgentHub/
    Planner/           # AgUiPlanProgressNotifier (implements IPlanProgressNotifier from Application)
    AgUi/              # New plan event subtypes added to existing AgUiEvents
```

### Key Design Decisions

1. **Extend existing orchestration** — The planner adds DAG capabilities alongside `RunOrchestratedTaskCommandHandler`, not replacing it. `ExecutePlanCommand` is a new entry point that coexists with `RunOrchestratedTaskCommand`. The `LlmCallStepExecutor` delegates to the existing `RunConversationCommand` via MediatR.
2. **EF Core as first-class dependency** — This is the first use of EF Core in the codebase. This is a deliberate architectural decision: plan state requires transactional updates, queryable audit trails, and checkpoint/resume that file-based stores cannot provide. EF Core's scoped `DbContext` lifetime requires careful handling when bridging with singleton services (use `IDbContextFactory<PlannerDbContext>` for singleton callers). Migrations applied via `context.Database.Migrate()` at startup with a configurable flag to disable for production environments preferring manual migrations.
3. **Two-tier sandbox** — Process+Job Objects (default, <10ms), Docker containers (elevated, ~100ms). Tier determined by tool capability profile and autonomy tier. If a tool declares `MinimumIsolation = Container` and Docker is unavailable, execution is **refused** (not downgraded). Fallback to process tier only for tools without explicit minimum isolation requirements.
4. **Attribute + appsettings override** for capability declarations — compile-time defaults, runtime restriction. Capability enforcement extends the existing `ToolPermissionBehavior` rather than adding a separate pipeline behavior.
5. **Non-blocking escalation** — blocked steps only pause their dependent subgraph; independent branches continue. `HumanGateStepExecutor` uses `QueueEscalationAsync` (non-blocking), transitions step to `Blocked`, and the plan executor polls for escalation resolution on each scheduling pass.
6. **Notification pattern for AG-UI** — Following existing codebase patterns (`AgUiDriftNotifier`, `AgUiEscalationNotifier`), define `IPlanProgressNotifier` in Application layer, implement `AgUiPlanProgressNotifier` in Presentation. Infrastructure executors call the Application interface, never Presentation types directly.

---

## Subsystem 1: Planner

### 1.1 Domain Models

#### PlanGraph

The central domain model representing a directed acyclic graph of plan steps.

```csharp
public record PlanGraph
{
    public required PlanId Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<PlanStep> Steps { get; init; }
    public required IReadOnlyList<PlanEdge> Edges { get; init; }
    public required PlanConfiguration Configuration { get; init; }
    public PlanId? ParentPlanId { get; init; }  // For sub-plan invocation
}
```

`PlanId` is a strongly-typed ID (value object wrapping `Guid`).

#### PlanStep

Each node in the graph. Step type determines which keyed executor handles it.

```csharp
public record PlanStep
{
    public required PlanStepId Id { get; init; }
    public required string Name { get; init; }
    public required StepType Type { get; init; }
    public required StepConfiguration Configuration { get; init; }
    public required RetryPolicy RetryPolicy { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public AutonomyLevel? RequiredAutonomyLevel { get; init; }
}
```

#### StepType Enum

```csharp
public enum StepType
{
    LlmCall,
    ToolUse,
    HumanGate,
    ConditionalBranch,
    SubPlanInvocation
}
```

#### StepConfiguration

Abstract record base in `Domain.AI/Planner/StepConfiguration.cs` with `JsonPolymorphicAttribute` and `JsonDerivedType` annotations for each subtype. EF Core persists this as a JSON column with the `type` discriminator for round-trip deserialization.

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LlmCallConfig), "llm_call")]
[JsonDerivedType(typeof(ToolUseConfig), "tool_use")]
[JsonDerivedType(typeof(HumanGateConfig), "human_gate")]
[JsonDerivedType(typeof(ConditionalBranchConfig), "conditional_branch")]
[JsonDerivedType(typeof(SubPlanConfig), "sub_plan")]
public abstract record StepConfiguration;
```

- `LlmCallConfig` — system prompt, model deployment key, temperature, max tokens
- `ToolUseConfig` — tool name (key), input parameters, sandbox isolation level override
- `HumanGateConfig` — escalation message, approval strategy key (`AnyOf`, `AllOf`, `Quorum`), timeout
- `ConditionalBranchConfig` — condition expression string (evaluated using the existing `DecisionRule` pattern from `Domain.Common/Workflow/DecisionRule.cs`), true/false edge targets. Condition expressions are validated and sanitized before execution — only JSON path comparisons, boolean operators, and null checks are permitted (no arbitrary code evaluation).
- `SubPlanConfig` — child plan ID or inline plan definition, context isolation settings

#### PlanEdge

Directed edge between steps.

```csharp
public record PlanEdge
{
    public required PlanStepId From { get; init; }
    public required PlanStepId To { get; init; }
    public required EdgeType Type { get; init; }
    public string? Condition { get; init; }  // For conditional edges
}

public enum EdgeType
{
    DataFlow,         // Output of From feeds as input to To
    ControlFlow,      // From must complete before To starts
    ConditionalTrue,  // Follow if condition evaluates true
    ConditionalFalse  // Follow if condition evaluates false
}
```

#### PlanExecutionState

Per-step state machine tracking execution progress.

```csharp
public enum StepExecutionStatus
{
    Pending,    // Not yet eligible to run
    Ready,      // All dependencies met, waiting for scheduler
    Running,    // Currently executing
    Completed,  // Finished successfully
    Failed,     // Finished with error (may retry)
    Skipped,    // Skipped due to upstream failure or conditional branch
    Blocked     // Waiting on escalation/human gate
}
```

```csharp
public record StepExecutionState
{
    public required PlanStepId StepId { get; init; }
    public required StepExecutionStatus Status { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public ToolExecutionAttestation? Attestation { get; init; }  // Nullable for non-tool steps and crashed executions
}
```

#### RetryPolicy

```csharp
public record RetryPolicy
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public BackoffStrategy Strategy { get; init; } = BackoffStrategy.Exponential;
    public ErrorRecovery OnExhausted { get; init; } = ErrorRecovery.FailStep;
}

public enum BackoffStrategy { Fixed, Linear, Exponential }
public enum ErrorRecovery { FailStep, SkipStep, FailPlan, Escalate }
```

#### PlanConfiguration

```csharp
public record PlanConfiguration
{
    public TimeSpan PlanTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public int MaxParallelSteps { get; init; } = 10;
    public int MaxSubPlanDepth { get; init; } = 5;  // Prevents unbounded recursion
}
```

### 1.2 Plan Generation

`IPlanGenerator` in `Application.AI.Common/Interfaces/Planner/` with `LlmPlanGeneratorService` in `Infrastructure.AI/Planner/`.

This addresses the "intake funnel" — how `PlanGraph` objects get created:

**Flow**:
1. `GeneratePlanCommand` takes a task description + optional constraints
2. The handler calls `IPlanGenerator.GenerateAsync()` which delegates to an LLM
3. The LLM receives the task description + JSON schema for `PlanGraph` output
4. The LLM generates structured JSON that maps to the plan domain models
5. `IPlanValidator` validates the generated plan before persisting
6. Returns `PlanId` on success

This extends the existing `RunOrchestratedTaskCommandHandler` pattern (which asks the LLM to decompose tasks) into a formal DAG structure. The orchestrator's `SUBTASK: [agent_name] - [description]` template becomes the LLM's context for generating plan steps.

Human-created plans are also supported via `CreatePlanCommand` which takes a pre-built `PlanGraph` directly.

### 1.3 Plan Validation

`IPlanValidator` performs pre-execution validation. Implementations:

**Cycle Detection** — Run topological sort (Kahn's algorithm). If the algorithm doesn't visit all nodes, a cycle exists. Return `Result<T>.Fail` with the nodes involved.

**Unreachable Node Detection** — BFS/DFS from all root nodes (nodes with no incoming edges). Any unvisited node is unreachable. Zero root nodes is a distinct error (implies a cycle or empty graph).

**Edge Referential Integrity** — Every edge's `From` and `To` must reference `PlanStepId` values that exist in the plan's `Steps` collection.

**Conditional Branch Completeness** — Every `ConditionalBranch` step must have both `ConditionalTrue` and `ConditionalFalse` outgoing edges. Missing either means execution can dead-end.

**Self-Referencing Sub-Plan Detection** — Check that `SubPlanConfig.ChildPlanId` does not reference the current plan or any ancestor plan (walk up `ParentPlanId` chain). Kahn's algorithm only catches intra-plan cycles, not cross-plan cycles.

**Step Configuration Validation** — FluentValidation per step type. Each `StepConfiguration` subtype has its own validator (e.g., `LlmCallConfigValidator` checks model deployment key exists, `ToolUseConfigValidator` checks tool key is registered).

**Resource Estimation** — Sum estimated durations across the critical path (longest chain). Report estimated wall-clock time and total compute time. This is informational, not blocking.

Validation runs via the `CreatePlanCommand` handler before persisting, and also before `ExecutePlanCommand` begins execution.

### 1.4 Plan Executor

`IPlanExecutor` is the core orchestration engine. Implementation: `PlanExecutor`.

**Algorithm — Dynamic Ready-Queue Model**:
1. Load plan graph and current execution state from `IPlanStateStore`
2. Initialize a ready queue: all steps whose dependencies are fully met (Completed or Skipped)
3. Use a `SemaphoreSlim` with `MaxParallelSteps` for bounded concurrency
4. For each step dequeued from the ready queue:
   a. Await the semaphore
   b. Resolve the appropriate `IPlanStepExecutor` via keyed DI on `StepType`
   c. Launch step execution as a background `Task`
   d. On step completion: persist state, notify via `IPlanProgressNotifier`, evaluate retry/skip/fail
   e. Check all downstream steps: if all their dependencies are now met, enqueue them
   f. Release the semaphore
5. Check for `Blocked` steps: poll `IEscalationService` for resolution. If resolved, transition to `Ready` and enqueue.
6. Continue until the ready queue is empty AND no steps are `Running` or `Blocked`, OR plan timeout fires.

This is a dynamic model — no layer abstraction. Steps are enqueued as soon as their dependencies are met, maximizing parallelism within the concurrency bound.

**Concurrency control**: Each plan execution acquires a `SemaphoreSlim` keyed by plan ID (from a `ConcurrentDictionary<PlanId, SemaphoreSlim>`). This prevents two concurrent `ExecutePlanCommand` calls from racing on the same plan.

**Sub-plan invocation**: The `SubPlanStepExecutor` creates a new DI scope (matching the existing `IServiceScopeFactory` pattern from `RunOrchestratedTaskCommandHandler`), instantiates a new `IPlanExecutor`, and awaits the child plan's completion. The child increments the depth counter; execution refuses if `MaxSubPlanDepth` is exceeded. The parent step blocks, but the parent plan continues executing other independent branches.

**Checkpoint**: After each step state transition, persist to SQLite via `IPlanStateStore`. On resume, load the last state and rebuild the ready queue from current step statuses.

### 1.5 Plan Step Executors

Five keyed DI registrations of `IPlanStepExecutor`:

**LlmCallStepExecutor** (`StepType.LlmCall`)
- Delegates to the existing `RunConversationCommand` via MediatR (same pattern the orchestrator uses)
- Builds messages from step config (system prompt + input from upstream steps)
- Streams tokens, notifying via `IPlanProgressNotifier`
- Returns the full response as step output

**ToolUseStepExecutor** (`StepType.ToolUse`)
- Routes through the sandbox subsystem via `ExecuteInSandboxCommand`
- Sandbox isolation level determined by tool's `ToolPermissionProfile` and step's autonomy tier
- Returns `ToolExecutionResult` including attestation

**HumanGateStepExecutor** (`StepType.HumanGate`)
- Calls `IEscalationService.QueueEscalationAsync()` (non-blocking)
- Transitions step to `Blocked` immediately
- Notifies via `IPlanProgressNotifier` with gate metadata (who needs to approve, what strategy)
- The plan executor polls for resolution on each scheduling pass
- When approved: transitions to Completed with approval details as output
- When rejected: transitions to Failed

**ConditionalBranchStepExecutor** (`StepType.ConditionalBranch`)
- Evaluates condition expression against upstream step outputs
- Reuses the existing `DecisionRule` evaluation pattern from `Domain.Common/Workflow/`
- Condition expressions are validated at plan creation time (whitelist of safe operations)
- Sets `StepExecutionStatus.Completed` and marks the appropriate conditional edge as active
- The plan executor uses the active edge to determine which downstream steps become Ready

**SubPlanStepExecutor** (`StepType.SubPlanInvocation`)
- Creates a new DI scope via `IServiceScopeFactory`
- Checks depth counter against `MaxSubPlanDepth` — refuses if exceeded
- Loads or creates the child plan
- Instantiates a fresh `IPlanExecutor` in the child scope
- Awaits child plan completion
- Returns the child plan's final output as the parent step's output
- Child plan gets its own `IPlanStateStore` entries (linked via `ParentPlanId`)

### 1.6 CQRS Commands and Queries

**Commands**:
- `GeneratePlanCommand` — LLM generates a plan from a task description. Returns `PlanId`.
- `CreatePlanCommand` — validate and persist a pre-built plan graph. Returns `PlanId`.
- `ExecutePlanCommand` — start or resume plan execution. Returns `Result<PlanExecutionSummary>`.
- `CancelPlanCommand` — cancel a running plan. Marks all non-terminal steps as Skipped.
- `RetryPlanStepCommand` — manually retry a failed step.

**Queries**:
- `GetPlanQuery` — retrieve plan graph and current execution state.
- `GetPlanHistoryQuery` — retrieve step execution history (audit trail).
- `ListPlansQuery` — list plans with filtering (status, date range).

Each command has a FluentValidation validator. Each handler returns `Result<T>`.

### 1.7 Persistence (EF Core + SQLite)

**Architectural note**: This is the first EF Core usage in the codebase. Previous persistence uses JSONL files and in-memory stores. Plan state requires transactional updates, queryable audit trails, and checkpoint/resume — capabilities that justify the dependency cost.

`PlannerDbContext` with these entity configurations:

- `PlanGraphEntity` — plan metadata, configuration JSON, parent plan FK, **`RowVersion` concurrency token** (optimistic concurrency)
- `PlanStepEntity` — step metadata, type, config JSON (with `type` discriminator for polymorphic deserialization), retry policy JSON
- `PlanEdgeEntity` — from/to step FKs, edge type, condition
- `StepExecutionStateEntity` — step FK, status, attempt count, timestamps, output, error, attestation JSON, **`RowVersion` concurrency token**
- `PlanExecutionLogEntity` — append-only audit log: plan ID, step ID, event type, timestamp, details JSON

**Migration strategy**: Code-first EF Core migrations. SQLite database file at configurable path (default: `{DataDir}/planner.db`). Applied via `context.Database.Migrate()` at startup (configurable: `Planner:AutoMigrate = true` for dev, `false` for production where manual `dotnet ef database update` is preferred).

**Lifetime management**: `PlannerDbContext` registered as scoped via `AddDbContext<T>`. Services that need cross-scope access (singleton drift/learnings bridging) use `IDbContextFactory<PlannerDbContext>` to create short-lived contexts.

**Concurrency**: SQLite WAL mode for read concurrency. Optimistic concurrency via EF Core `RowVersion` tokens on `PlanGraphEntity` and `StepExecutionStateEntity`. Concurrent `ExecutePlanCommand` calls for the same plan are serialized via a keyed `SemaphoreSlim` in `PlanExecutor`.

### 1.8 Governance Integration

**Pre-step check**: Before executing any step, the plan executor resolves the step's autonomy level. Since `IAutonomyTierResolver` accepts `SubagentType` or `SubagentDefinition` (not plan step context), add a new overload: `Resolve(PlanStepContext)` that maps step types to equivalent autonomy contexts (e.g., `StepType.ToolUse` maps to the tool's registered agent type, `StepType.LlmCall` maps to the chat client's agent type). If the step's required autonomy level exceeds the granted level, the step transitions to `Blocked` and an escalation is queued via `IEscalationService.QueueEscalationAsync()`.

**Post-step telemetry**: After each step, feed success/failure data to `IDriftDetectionService.EvaluateDriftAsync()` via `IDbContextFactory<PlannerDbContext>` (since drift service may be singleton).

**Pattern learning**: On plan completion, if the plan succeeded and had a novel structure (not matching known patterns), capture it via `RememberCommand` in the learnings subsystem.

### 1.9 AG-UI Event Mapping

**Architecture**: Following existing notification patterns (`AgUiDriftNotifier`, `AgUiEscalationNotifier`, `AgUiLearningNotifier`), define `IPlanProgressNotifier` in `Application.AI.Common/Interfaces/Planner/`. Implement `AgUiPlanProgressNotifier` in `Presentation.AgentHub/Planner/` which converts notifications to AG-UI events and writes them via the existing `AgUiEventWriter`.

Infrastructure executors call `IPlanProgressNotifier` methods, never Presentation types.

Add new polymorphic subtypes to `AgUiEvents.cs`:

| Plan Event | AG-UI Mapping | Payload |
|------------|---------------|---------|
| `PlanStartedEvent` | `RUN_STARTED` | Plan ID, name, full graph as `STATE_SNAPSHOT` |
| `PlanStepStartedEvent` | `STEP_STARTED` | Step ID, name, type |
| `PlanStepCompletedEvent` | `STEP_FINISHED` | Step ID, status, duration, output summary |
| `PlanStateUpdateEvent` | `STATE_DELTA` | JSON Patch (RFC 6902) with step status transition |
| `SandboxStatusEvent` | `CUSTOM` | Tool name, isolation level, resource usage, attestation hash |
| `PlanCompletedEvent` | `RUN_FINISHED` | Plan ID, total duration, step summary |
| `PlanFailedEvent` | `RUN_ERROR` | Plan ID, failed step, error message |

Events flow through the existing `AgUiEventWriter` and SSE endpoint. No new transport needed.

---

## Subsystem 2: Code Sandbox

### 2.1 Domain Models

#### ToolCapability

```csharp
[Flags]
public enum ToolCapability
{
    None           = 0,
    FileRead       = 1 << 0,
    FileWrite      = 1 << 1,
    NetworkAccess  = 1 << 2,
    Subprocess     = 1 << 3,
    EnvRead        = 1 << 4,
    DatabaseRead   = 1 << 5,
    DatabaseWrite  = 1 << 6,
    LlmInvocation  = 1 << 7
}
```

#### ToolPermissionProfile

```csharp
public record ToolPermissionProfile
{
    public required ToolCapability RequiredCapabilities { get; init; }
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
    public IReadOnlyList<string> AllowedPrograms { get; init; } = [];
    public IReadOnlyList<string> DeniedPaths { get; init; } = [];
    public IReadOnlyList<string> DeniedHosts { get; init; } = [];
    public SandboxIsolationLevel MinimumIsolation { get; init; } = SandboxIsolationLevel.Process;
}
```

#### SandboxIsolationLevel

```csharp
public enum SandboxIsolationLevel
{
    None,       // Direct execution (existing behavior for safe, read-only tools)
    Process,    // Subprocess with Job Object resource limits (default)
    Container   // Docker container with full isolation (elevated)
}
```

#### ToolCapabilityAttribute

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class ToolCapabilityAttribute : Attribute
{
    public ToolCapability Capabilities { get; }
    public SandboxIsolationLevel MinimumIsolation { get; init; } = SandboxIsolationLevel.Process;
}
```

#### ToolExecutionAttestation

```csharp
public record ToolExecutionAttestation
{
    public required string ToolName { get; init; }
    public required string InputHash { get; init; }    // SHA-256
    public string? OutputHash { get; init; }           // SHA-256, null if execution crashed
    public required DateTimeOffset Timestamp { get; init; }
    public required string Signature { get; init; }    // HMAC-SHA256
    public required string KeyVersion { get; init; }   // For key rotation verification
    public bool IsFailureAttestation { get; init; }    // True when output unavailable
    public string? FailureReason { get; init; }        // Populated for failure attestations
}
```

### 2.2 Capability Enforcement

**Extends existing `ToolPermissionBehavior`** in `Application.AI.Common/` rather than adding a new pipeline behavior. The existing behavior already intercepts tool execution requests via `IToolRequest`. Phase 4 adds capability-based checks to this existing behavior:

**Enhanced flow**:
1. Resolve the tool's `ToolPermissionProfile` — first check appsettings override, fall back to `[ToolCapabilityAttribute]` on the tool class
2. Get the session's granted capabilities (from the executing agent's autonomy tier + plan step context)
3. Compute `missing = profile.RequiredCapabilities & ~granted`. If non-zero, return `Result<T>.Fail` with the missing capabilities.
4. Validate scoped access: check requested paths against `AllowedPaths` / `DeniedPaths`, requested hosts against `AllowedHosts` / `DeniedHosts`. Deny overrides allow (Deno-style).
5. If all checks pass, invoke `next()` in the pipeline.

The existing `GovernancePolicyBehavior` continues to handle autonomy tier checks. `ToolPermissionBehavior` handles capability checks. Clear separation: governance = who is allowed to act, capability = what actions are allowed.

**appsettings override format**:
```json
{
  "Sandbox": {
    "ToolOverrides": {
      "file_system": {
        "DeniedCapabilities": ["NetworkAccess", "Subprocess"],
        "AllowedPaths": ["./workspace"],
        "MinimumIsolation": "Process"
      }
    }
  }
}
```

### 2.3 Sandbox Executor Interface

```csharp
public interface ISandboxExecutor
{
    Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken);
}
```

```csharp
public record SandboxExecutionRequest
{
    public required string ToolName { get; init; }
    public required string Input { get; init; }
    public required SandboxIsolationLevel IsolationLevel { get; init; }
    public required ToolPermissionProfile Permissions { get; init; }
    public required ResourceLimits ResourceLimits { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

public record ResourceLimits
{
    public long MemoryLimitBytes { get; init; } = 256 * 1024 * 1024; // 256MB
    public double CpuTimeSeconds { get; init; } = 30;
    public int MaxSubprocesses { get; init; } = 5;
    public long DiskQuotaBytes { get; init; } = 100 * 1024 * 1024; // 100MB
}

public record SandboxExecutionResult
{
    public required bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public required ResourceUsage ActualUsage { get; init; }
    public ToolExecutionAttestation? Attestation { get; init; }  // Nullable for crashed executions
    public TimeSpan Duration { get; init; }
}
```

Two keyed DI registrations: `ProcessSandboxExecutor` and `DockerSandboxExecutor`.

### 2.4 Process Sandbox (Default Tier)

`ProcessSandboxExecutor` — subprocess-based tool execution with Windows Job Objects.

**Execution flow**:
1. Create a temporary workspace directory (scoped, quota-monitored)
2. Serialize tool input to stdin JSON
3. Start `System.Diagnostics.Process` with redirected stdin/stdout/stderr
4. Create a Windows Job Object via `WindowsJobObjectManager` (P/Invoke wrapper)
5. Assign the process to the Job Object with configured limits:
   - `JOB_OBJECT_LIMIT_PROCESS_TIME` — CPU time limit from `ResourceLimits.CpuTimeSeconds`
   - `JOB_OBJECT_LIMIT_PROCESS_MEMORY` — memory limit from `ResourceLimits.MemoryLimitBytes`
   - `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` — guarantee cleanup
   - `JOB_OBJECT_LIMIT_ACTIVE_PROCESS` — subprocess count from `ResourceLimits.MaxSubprocesses`
6. Write input to stdin, close stdin
7. Read stdout/stderr concurrently with a combined timeout
8. On timeout: `Process.Kill(entireProcessTree: true)` as backstop (Job Object handles it, but belt-and-suspenders)
9. Compute attestation (hash input + output, HMAC sign). If process crashed with no output, create a failure attestation with input hash + error description.
10. Clean up workspace directory
11. Return `SandboxExecutionResult`

**WindowsJobObjectManager** — a `IDisposable` wrapper around Win32 Job Object APIs:
- `CreateJobObject` / `SetInformationJobObject` / `AssignProcessToJobObject` via P/Invoke
- Implements `IDisposable` to close the job handle (triggers `KILL_ON_JOB_CLOSE`)
- Exposes `QueryInformationJobObject` for resource usage reporting

**Cross-platform behavior**: Job Object implementation is behind `IProcessResourceLimiter` (in `Application.AI.Common/Interfaces/Sandbox/`). Phase 4 implements `WindowsProcessResourceLimiter` only. On Linux, `ProcessSandboxExecutor` still executes tools via `System.Diagnostics.Process` but skips resource limits with a logged warning (`ILogger.LogWarning("Process resource limits not available on {OS}. Tools execute without OS-level caps. Use container isolation for resource enforcement.", ...)`). This is a conscious trade-off for Phase 4 — container isolation IS the cross-platform answer for strict resource control. The `IProcessResourceLimiter` interface enables a future cgroups-based Linux implementation.

**Architectural note**: P/Invoke for Win32 Job Objects adds platform-specific native interop to what is currently a pure managed codebase. This is isolated behind `IProcessResourceLimiter` and conditionally compiled or runtime-switched to minimize impact.

### 2.5 Docker Sandbox (Elevated Tier)

`DockerSandboxExecutor` — container-based isolation via Docker.DotNet SDK.

**Execution flow**:
1. Create a temporary workspace directory with tool input file
2. Create container via `IDockerClient.Containers.CreateContainerAsync()`:
   - Image: from config (`Sandbox:Container:DefaultImage`) or per-tool override
   - `HostConfig.Memory` from `ResourceLimits.MemoryLimitBytes`
   - `HostConfig.NanoCPUs` from `ResourceLimits.CpuTimeSeconds` (converted)
   - `HostConfig.NetworkMode = "none"` (default, overridable for tools with `NetworkAccess` capability)
   - `HostConfig.ReadonlyRootfs = true`
   - `HostConfig.AutoRemove = true`
   - Bind mount: workspace dir -> `/workspace` (read-write)
3. Start container
4. Attach stdout/stderr streams
5. Wait for completion with timeout
6. On timeout: `StopContainerAsync` with kill grace period
7. Read output from workspace or stdout
8. Compute attestation (or failure attestation if container crashed)
9. Return `SandboxExecutionResult`

**Image management**: The harness ships a Dockerfile for the default sandbox image (minimal runtime, tool dependencies). Additional images can be configured per tool. Image availability is checked at startup.

**Docker availability and security enforcement**: `DockerSandboxExecutor` checks Docker daemon connectivity at registration time. If Docker is not available:
- Tools with `MinimumIsolation = Container` (declared via attribute): execution is **refused** with `Result<T>.Fail("Tool requires container isolation but Docker is unavailable")`. This is a security boundary — tool authors who declare container isolation have determined it's necessary for safety. Downgrading is not permitted.
- Tools without explicit minimum isolation: fall back to process isolation with a logged warning.

### 2.6 Execution Attestation

`IAttestationService` with `HmacAttestationService` implementation.

**Signing flow**:
1. Hash the tool input with SHA-256 -> `InputHash`
2. Hash the tool output with SHA-256 -> `OutputHash` (or null if crashed)
3. Concatenate `ToolName|InputHash|OutputHash|Timestamp` as the signing payload
4. Sign with HMAC-SHA256 using a versioned key
5. Return `ToolExecutionAttestation` record

**Key management**: Signing secret sourced from User Secrets (dev) or Azure Key Vault (prod) — never plaintext in appsettings.json (per existing security rules). Key version stored in attestation for rotation support. On key rotation, old keys are retained in a read-only verification keychain so historical attestations remain verifiable. New attestations use the current key version.

**Failure attestations**: When a process crashes (OOM, timeout, segfault), create an attestation with `InputHash` + `IsFailureAttestation = true` + `FailureReason`. No `OutputHash` since there's no output to hash. The attestation still proves the attempt was made and what input was provided.

**Verification**: Before tool output flows into agent reasoning (in `ToolUseStepExecutor`), verify the attestation by recomputing the HMAC using the key version in the attestation record. If verification fails, the step fails with a tamper-detection error.

**Storage**: Attestations are persisted in the `PlannerDbContext` as part of `StepExecutionStateEntity`. Queryable via `GetPlanHistoryQuery`.

### 2.7 Sandbox-Planner Integration

The `ToolUseStepExecutor` bridges the planner and sandbox:

1. Resolve the tool's `ToolPermissionProfile` (attribute + appsettings merge)
2. Determine isolation level:
   - Start with `ToolPermissionProfile.MinimumIsolation`
   - Elevate if the step's autonomy tier is `Supervised` or lower
   - Elevate if appsettings override specifies higher isolation
   - **Never downgrade** below the tool's declared minimum
3. Build `SandboxExecutionRequest` from step config + resolved profile + resource limits
4. Dispatch to the appropriate `ISandboxExecutor` via keyed DI on `SandboxIsolationLevel`
5. Receive `SandboxExecutionResult` including attestation
6. Verify attestation
7. Feed result back to plan step state (including resource usage for drift monitoring)

### 2.8 Integration with Existing Tool System

The sandbox does NOT replace `BatchedToolExecutionStrategy`. Instead:

- `BatchedToolExecutionStrategy` continues to handle direct tool execution for the agent loop (non-plan execution)
- When tools execute within a plan, `ToolUseStepExecutor` routes through the sandbox
- The `IToolConcurrencyClassifier` information feeds into capability resolution (read-only tools get lower isolation defaults)
- `FileSystemService` remains the file I/O implementation within the sandbox workspace

---

## Cross-Cutting Implementation

### DI Registration

Add to `Infrastructure.AI/DependencyInjection.cs`:

```csharp
// Planner
services.AddScoped<IPlanExecutor, PlanExecutor>();
services.AddScoped<IPlanValidator, PlanValidator>();
services.AddScoped<IPlanGenerator, LlmPlanGeneratorService>();
services.AddScoped<IPlanStateStore, EfCorePlanStateStore>();

// Plan step executors (keyed by StepType)
services.AddKeyedScoped<IPlanStepExecutor>(StepType.LlmCall, ...);
services.AddKeyedScoped<IPlanStepExecutor>(StepType.ToolUse, ...);
services.AddKeyedScoped<IPlanStepExecutor>(StepType.HumanGate, ...);
services.AddKeyedScoped<IPlanStepExecutor>(StepType.ConditionalBranch, ...);
services.AddKeyedScoped<IPlanStepExecutor>(StepType.SubPlanInvocation, ...);

// Sandbox executors (keyed by SandboxIsolationLevel)
services.AddKeyedScoped<ISandboxExecutor>(SandboxIsolationLevel.Process, ...);
services.AddKeyedScoped<ISandboxExecutor>(SandboxIsolationLevel.Container, ...);

// Attestation
services.AddScoped<IAttestationService, HmacAttestationService>();

// Notification (Application interface)
services.AddScoped<IPlanProgressNotifier, AgUiPlanProgressNotifier>();  // In Presentation DI

// EF Core with factory support for singleton callers
services.AddDbContext<PlannerDbContext>(options => options.UseSqlite(connectionString));
services.AddDbContextFactory<PlannerDbContext>();
```

### Configuration

All planner and sandbox settings under `Planner` and `Sandbox` sections of `appsettings.json`. Bind to strongly-typed options via `IOptionsMonitor<PlannerOptions>` and `IOptionsMonitor<SandboxOptions>`.

### Observability

**Traces**:
- `PlanExecution` activity source with spans: `plan.execute`, `plan.step.{type}`, `sandbox.execute`, `attestation.sign`
- Each span includes plan ID, step ID, step type, isolation level as tags

**Metrics** (counter/histogram):
- `planner.plan.executions` (counter, tags: status)
- `planner.step.executions` (counter, tags: type, status)
- `planner.step.duration` (histogram, tags: type)
- `sandbox.executions` (counter, tags: isolation_level, status)
- `sandbox.resource.memory` (histogram, tags: tool_name)
- `sandbox.resource.cpu` (histogram, tags: tool_name)
- `attestation.verifications` (counter, tags: result)

---

## Implementation Order

The implementation should proceed in this order, with each section building on the previous:

1. **Domain models** — All types in `Domain.AI/Planner/`, `Domain.AI/Sandbox/`, `Domain.AI/Attestation/`
2. **Application interfaces** — All interfaces in `Application.AI.Common/Interfaces/Planner/`, `Sandbox/`, `Attestation/` including `IPlanProgressNotifier`, `IProcessResourceLimiter`
3. **Plan validation** — `IPlanValidator` implementation + FluentValidation validators (all 7 validation checks)
4. **EF Core persistence** — `PlannerDbContext`, entity configs with RowVersion concurrency tokens, migrations, `IDbContextFactory` registration
5. **Plan state store** — `EfCorePlanStateStore` implementation
6. **Capability model** — Extend `ToolPermissionBehavior` with capability checks, `ToolPermissionProfile` resolution from attribute + appsettings
7. **Process sandbox** — `ProcessSandboxExecutor` + `WindowsJobObjectManager` behind `IProcessResourceLimiter`
8. **Docker sandbox** — `DockerSandboxExecutor` via Docker.DotNet with security enforcement (no downgrade from declared minimum)
9. **Attestation service** — `HmacAttestationService` with failure attestation support, key rotation, Key Vault integration
10. **Plan step executors** — All 5 keyed executors (HumanGate uses QueueEscalationAsync, SubPlan enforces MaxSubPlanDepth)
11. **Plan executor** — Dynamic ready-queue DAG engine with keyed SemaphoreSlim concurrency control
12. **Plan generator** — `LlmPlanGeneratorService` extending orchestrator decomposition pattern
13. **CQRS commands/queries** — All commands and queries with validators
14. **AG-UI events** — `AgUiPlanProgressNotifier` implementing `IPlanProgressNotifier`, new event subtypes
15. **DI registration** — Wire everything in `DependencyInjection.cs` files across layers
16. **Configuration** — `appsettings.json` sections, options binding, PlannerOptions/SandboxOptions
