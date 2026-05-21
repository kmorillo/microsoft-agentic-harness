# Phase 4: Planner & Code Sandbox

## Context

This is Phase 4 (final) of the 4-phase platform gaps roadmap for the Microsoft Agentic Harness. Phases 1-3 are complete and merged to main:
- Phase 1: Autonomy Tiers + Supervisor Agent
- Phase 2: Human Escalation + Fallback Chains
- Phase 3: Drift Detection + Learnings Log

The harness is a production-grade template for Microsoft Agent Framework agents with Clean Architecture, CQRS/MediatR, and keyed DI throughout.

## Goal

Add the final two capabilities that transform the harness from "agents that respond" to "agents that plan and safely execute": a structured workflow planner with DAG-based task decomposition, and a sandboxed execution environment for untrusted tool operations.

## Subsystem 1: Planner (~35% remaining)

### What exists today
- `MultiAgentWorkflow` — static fan-out/fan-in workflow builder with MEF-based `WorkflowBuilder` and `ExecutorBinding`
- `AgentExecutorFactory` — factory wrapping `IChatClient` calls as workflow executors
- `RunOrchestratedTaskCommandHandler` — dynamic LLM-driven orchestration: Phase 1 decomposes tasks via orchestrator agent, Phase 2 delegates subtasks to sub-agents, Phase 3 synthesizes results
- `SUBTASK: [agent_name] - [description]` prompt template for task decomposition
- Integration with `IAgentFactory`, multi-turn conversation, sub-agent delegation, result aggregation

### What's missing
- **Workflow DAG representation** — formal graph model for multi-step plans with typed nodes (LLM call, tool use, human gate, conditional branch), edges with dependency semantics, and topological execution ordering
- **Step retry and branching** — conditional branches, loops, error recovery strategies per step, configurable retry policies beyond catch-and-log
- **Task state persistence** — checkpoint/resume for long-running workflows, audit trail per step, intermediate result storage
- **Execution scheduling** — step-level parallelism based on DAG dependencies (not just fan-out/fan-in), bounded concurrency, priority queuing
- **Governance integration** — per-step autonomy tier checks, escalation triggers at step boundaries, supervisor oversight hooks
- **Plan validation** — cycle detection, unreachable node detection, resource estimation before execution
- **AG-UI events** — real-time SSE events for plan progress, step transitions, completion/failure notifications

## Subsystem 2: Code Sandbox (~55% remaining)

### What exists today
- `IToolExecutionStrategy` + `BatchedToolExecutionStrategy` — batched tool execution with read/write partitioning, `SemaphoreSlim` for bounded parallel reads, sequential writes
- `IToolConcurrencyClassifier` + `ToolConcurrencyClassifier` — classifies tools via `ITool.IsReadOnly` and `ITool.IsConcurrencySafe` metadata
- `IFileSystemService` + `FileSystemService` — path-based sandbox with blocklist (system dirs, `.git`, `node_modules`), 10 MB file limit, symlink resolution, search caps

### What's missing
- **Process isolation** — subprocess-based tool execution for untrusted operations, with stdin/stdout protocol for communication
- **Resource limits** — per-tool CPU time limits, memory caps, disk quota enforcement, network access control
- **Capability model** — declarative per-tool permission matrix (file I/O, network, subprocess, environment variables) checked before execution
- **Timeout enforcement** — hard kill after configurable timeout at the sandbox layer (not just CancellationToken cooperation)
- **Execution attestation** — hash/sign tool outputs, tamper detection for results flowing back into agent reasoning
- **Container support (optional)** — Docker/container-based isolation for highest-risk tools, with image management and resource pinning
- **AG-UI events** — sandbox status events (tool started, resource warning, timeout, completion)

## Cross-Cutting Concerns

- **Planner informs Sandbox**: when a plan step involves untrusted tools, the planner routes execution through the sandbox with appropriate isolation level
- **Sandbox informs Planner**: sandbox execution results (including resource usage, timing, errors) feed back into planner step state for retry decisions
- **Governance bridge**: both subsystems integrate with Phase 1 autonomy tiers and Phase 2 escalation for permission checks and human gates
- **Quality loop**: Phase 3 drift detection monitors planner success rates; learnings capture effective plan patterns
- **AG-UI events**: both subsystems emit SSE events for real-time frontend updates
- **Observability**: OpenTelemetry traces and metrics for plan execution and sandbox operations
- **Configuration**: `appsettings.json` blocks for planner limits, sandbox resource caps, isolation levels
- **Testing**: 80%+ coverage, TDD approach, integration tests with in-memory stores

## Architecture Constraints

- Clean Architecture layers: Domain models first, Application interfaces, Infrastructure implementations
- CQRS/MediatR for commands and queries
- Keyed DI for strategy registration (plan step executors, sandbox isolation levels)
- FluentValidation on all DTOs
- Result<T> pattern for error handling
- Immutability: records, init-only properties, IReadOnlyList<T>

## Dependencies

- Phase 1 autonomy tiers (for per-step permission checks)
- Phase 2 escalation system (for human gates in plans, sandbox violations)
- Phase 3 drift detection (for monitoring planner quality over time)
- Phase 3 learnings (for capturing effective plan patterns)
- Existing `MultiAgentWorkflow` and `RunOrchestratedTaskCommandHandler` (foundation for planner)
- Existing `BatchedToolExecutionStrategy` and `FileSystemService` (foundation for sandbox)
