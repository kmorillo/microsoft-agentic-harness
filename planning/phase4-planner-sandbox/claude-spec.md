# Phase 4: Planner & Code Sandbox — Complete Specification

## Context

Phase 4 is the final phase of the 4-phase platform gaps roadmap for the Microsoft Agentic Harness. Phases 1-3 are complete and merged:
- Phase 1: Autonomy Tiers + Supervisor Agent
- Phase 2: Human Escalation + Fallback Chains
- Phase 3: Drift Detection + Learnings Log

The harness is a production-grade .NET 10 template using Clean Architecture, CQRS/MediatR, keyed DI, and FluentValidation.

## Goal

Add structured workflow planning with DAG-based task decomposition and sandboxed execution for untrusted tool operations. Transform the harness from "agents that respond" to "agents that plan and safely execute."

---

## Subsystem 1: Planner

### Architecture Decision

**Extend the existing RunOrchestratedTaskCommandHandler** with DAG capabilities rather than creating a parallel subsystem. The current handler already does LLM-driven task decomposition (Phase 1 -> decompose, Phase 2 -> delegate, Phase 3 -> synthesize). Phase 4 adds formal graph representation, dependency tracking, parallel execution based on DAG structure, and state persistence.

**Foundation**: MAF WorkflowBuilder for execution, custom Kahn's algorithm for DAG validation and parallel layer computation.

### Plan Step Types (5)

1. **LLM Call** — invoke a chat client with prompt/context, stream tokens
2. **Tool Use** — execute a registered tool (routed through sandbox based on capability profile)
3. **Human Gate** — escalation point requiring human approval before proceeding (integrates with Phase 2 `IEscalationService`)
4. **Conditional Branch** — evaluate a condition against step outputs to determine which edge to follow. Conditions are expression-based (code-evaluated, not LLM-evaluated)
5. **Sub-Plan Invocation** — invoke another plan as a step. Child plan gets isolated context (own sandbox scope, own escalation context). Parent step blocks until child completes.

### Workflow DAG Model

**Domain layer models**:
- `PlanGraph` — directed acyclic graph of plan steps with typed nodes and edges
- `PlanStep` — node with step type, configuration, retry policy, timeout, autonomy tier
- `PlanEdge` — directed edge with dependency semantics (data flow, control flow, conditional)
- `StepType` — enum of the 5 step types above
- `PlanExecutionState` — per-step state machine: `Pending -> Ready -> Running -> Completed | Failed | Skipped | Blocked`

**Application layer**:
- `IPlanExecutor` — orchestrates plan execution using DAG dependencies
- `IPlanStepExecutor` — keyed DI interface, one implementation per `StepType`
- `IPlanValidator` — validates plan before execution (cycle detection, unreachable nodes, resource estimation)
- `IPlanStateStore` — persistence interface for plan execution state

**Infrastructure layer**:
- `PlanExecutor` — topological sort into layers, `Task.WhenAll` within layers, respects bounded concurrency
- Keyed `IPlanStepExecutor` implementations: `LlmCallStepExecutor`, `ToolUseStepExecutor`, `HumanGateStepExecutor`, `ConditionalBranchStepExecutor`, `SubPlanStepExecutor`
- `EfCorePlanStateStore` — SQLite via EF Core for checkpoint/resume and audit trail

### Step Retry and Branching

- Per-step configurable retry policy (max retries, backoff strategy, retry-on conditions)
- Conditional branches evaluated against previous step outputs using expression evaluation
- Failed steps mark dependent subgraph as `Skipped` (not the entire plan)
- Error recovery: configurable per step (retry, skip, fail-plan, escalate)

### Task State Persistence

- **SQLite via EF Core** — transactional state updates, queryable audit trail
- Checkpoint after each step completion/failure
- Resume from last checkpoint on restart
- Audit trail: step transitions with timestamps, inputs, outputs, errors
- Plan execution history for drift detection integration

### Execution Scheduling

- Topological sort groups steps into dependency layers
- Steps within a layer execute in parallel via `Task.WhenAll`
- Bounded concurrency: configurable max parallel steps (default: 10)
- Priority queuing: steps with lower autonomy tier requirements execute first within a layer

### Governance Integration

- Per-step autonomy tier check via `IAutonomyTierResolver` before execution
- Human gate steps route through `IEscalationService` (blocking mode)
- **Non-blocking escalation**: when a step is blocked on escalation, independent parallel branches continue executing. Only the dependent subgraph blocks.
- Supervisor oversight hooks: pre-step and post-step governance callbacks
- Drift detection: plan success/failure rates fed to `IDriftDetectionService`
- Learnings: effective plan patterns captured via `RememberCommand`

### Plan Validation

- Cycle detection (topological sort failure)
- Unreachable node detection (nodes with no path from start)
- Resource estimation: total estimated time/cost before execution
- Step type validation: required configuration per step type
- FluentValidation on all plan DTOs

### Scale

- 10-50 concurrent plan executions
- 10-50 steps per plan
- Bounded concurrency prevents resource exhaustion

### Timeouts

- **Per-step default**: 60 seconds (configurable per step)
- **Per-plan default**: 30 minutes (configurable per plan)
- Hard enforcement: `CancellationTokenSource` with timeout, backstopped by process kill for sandbox steps

---

## Subsystem 2: Code Sandbox

### Architecture Decision

**Two-tier isolation approach**:
1. **Default (Process + Job Objects)**: `System.Diagnostics.Process` with Windows Job Objects for resource limits. Fast (<10ms startup), sufficient for trusted tools with known behavior.
2. **Elevated (Docker containers)**: Docker.DotNet for untrusted/LLM-generated code. `--network=none`, memory/CPU caps, auto-remove. ~100ms startup.

Sandbox tier determined by tool's `ToolPermissionProfile` and the plan step's autonomy tier.

### Capability Model

**Declaration**: Attribute + appsettings override pattern.
- Tools declare default capabilities via `[ToolCapability]` attribute
- `appsettings.json` can override/restrict capabilities per environment (e.g., production restricts network access)
- Deny overrides allow (Deno-style)

**Capability enum** (`[Flags]`):
- `FileRead` — read from scoped paths
- `FileWrite` — write to scoped paths
- `NetworkAccess` — HTTP to allowlisted hosts
- `Subprocess` — launch child processes
- `EnvRead` — read environment variables
- `DatabaseRead` — query databases
- `DatabaseWrite` — mutate databases
- `LlmInvocation` — call LLM APIs

**ToolPermissionProfile**: required capabilities + scoped allowlists (paths, hosts, programs) + deny lists

**Enforcement**: `ToolCapabilityBehavior<TRequest, TResponse>` MediatR pipeline behavior. Fast reject on missing capabilities, fine-grained scope validation before tool execution.

### Process Isolation (Default Tier)

- `System.Diagnostics.Process` for subprocess-based tool execution
- stdin/stdout JSON protocol for communication
- Windows Job Objects via P/Invoke:
  - `JOB_OBJECT_LIMIT_PROCESS_TIME` — CPU time limit
  - `JOB_OBJECT_LIMIT_PROCESS_MEMORY` — memory cap
  - `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` — hard kill guarantee
  - `JOB_OBJECT_LIMIT_ACTIVE_PROCESS` — subprocess count limit
- Linux: cgroups v2 equivalent (future, not Phase 4 scope on Windows)

### Container Isolation (Elevated Tier)

- Docker.DotNet SDK for container lifecycle
- Per-execution container with:
  - `NetworkMode = "none"` (no network by default)
  - Memory limit (default: 256MB)
  - CPU limit (default: 0.5 CPU)
  - `ReadonlyRootfs = true`
  - `AutoRemove = true`
  - Scoped workspace volume mount
- Image management: pre-built sandbox images with tool runtimes
- Resource pinning: configurable per tool or per plan step

### Resource Limits

- Per-tool CPU time limits (Job Object or container CPU quota)
- Per-tool memory caps (Job Object or container memory limit)
- Disk quota enforcement via scoped workspace with size monitoring
- Network access control: default-deny, allowlist per tool capability profile

### Timeout Enforcement

- **Hard kill pattern**: `CancellationTokenSource` with timeout + background `Task.Delay` calling `Process.Kill(entireProcessTree: true)` as backstop
- Job Objects provide OS-level guarantee via `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`
- Docker: `StopContainerAsync` with kill timeout
- Default: 60s per tool execution (configurable)

### Execution Attestation

- **Full HMAC-SHA256 signing** of tool outputs included in Phase 4
- `ToolExecutionAttestation` record: tool name, input hash (SHA-256), output hash (SHA-256), timestamp, HMAC signature
- Per-session signing key derived from session context
- Attestation chain stored alongside execution logs in SQLite
- Tamper detection: verify attestation before tool output flows into agent reasoning
- Audit trail: queryable attestation history per plan execution

### Sandbox-Planner Integration

- When a plan step involves untrusted tools, the planner routes execution through the sandbox with appropriate isolation level
- Sandbox execution results (resource usage, timing, errors, attestation) feed back into planner step state for retry decisions
- Autonomy tier determines sandbox tier: higher autonomy -> lower isolation, supervised -> elevated isolation

---

## Cross-Cutting Concerns

### AG-UI Events

**Extend existing AgUiEvents** with new plan-specific subtypes:
- `PlanStartedEvent` — maps to AG-UI `RUN_STARTED`, includes full plan graph as `STATE_SNAPSHOT`
- `PlanStepStartedEvent` — maps to `STEP_STARTED`
- `PlanStepCompletedEvent` — maps to `STEP_FINISHED`
- `PlanStateUpdateEvent` — maps to `STATE_DELTA` (RFC 6902 JSON Patch for step status transitions)
- `SandboxStatusEvent` — custom event for tool sandbox lifecycle (started, resource warning, timeout, completed)
- `PlanCompletedEvent` — maps to `RUN_FINISHED`
- `PlanFailedEvent` — maps to `RUN_ERROR`

Use `STATE_SNAPSHOT` at plan start (full graph), `STATE_DELTA` for each step transition. Frontend gets live patchable view of plan execution.

### Observability

- OpenTelemetry traces: span per plan execution, child spans per step
- Metrics: plan duration, step duration, success/failure rates, sandbox resource usage, attestation verification rates
- Structured logging: plan ID, step ID, step type in all log entries

### Configuration

```json
{
  "Planner": {
    "MaxConcurrentPlans": 50,
    "MaxParallelSteps": 10,
    "DefaultStepTimeoutSeconds": 60,
    "DefaultPlanTimeoutMinutes": 30,
    "MaxStepsPerPlan": 100
  },
  "Sandbox": {
    "DefaultIsolationLevel": "Process",
    "Process": {
      "DefaultMemoryLimitMb": 256,
      "DefaultCpuTimeLimitSeconds": 30,
      "MaxActiveSubprocesses": 5
    },
    "Container": {
      "DefaultMemoryLimitMb": 256,
      "DefaultCpuLimit": 0.5,
      "DefaultImage": "tool-sandbox:latest",
      "NetworkMode": "none",
      "AutoRemove": true
    },
    "Attestation": {
      "Enabled": true,
      "Algorithm": "HMACSHA256"
    }
  }
}
```

### Testing

- 80%+ coverage, TDD approach
- Unit tests: plan validation, DAG operations, capability enforcement, attestation
- Integration tests: plan execution with in-memory SQLite, sandbox with mock processes
- E2E: full plan execution through AG-UI event stream

### Architecture Constraints

- Clean Architecture: Domain models first, Application interfaces, Infrastructure implementations
- CQRS/MediatR for plan commands and queries
- Keyed DI for step executors (one per `StepType`), sandbox isolation levels, capability enforcement
- FluentValidation on all DTOs
- Result<T> for error handling
- Immutability: records, init-only properties, IReadOnlyList<T>

### Dependencies

- Phase 1 autonomy tiers (per-step permission checks)
- Phase 2 escalation (human gates, sandbox violations)
- Phase 3 drift detection (plan quality monitoring)
- Phase 3 learnings (effective plan pattern capture)
- Existing `RunOrchestratedTaskCommandHandler` (foundation for planner extension)
- Existing `BatchedToolExecutionStrategy` and `FileSystemService` (foundation for sandbox)
- Existing `AgUiEventWriter` and `AgUiEvents` (foundation for plan events)
