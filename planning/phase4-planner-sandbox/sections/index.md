<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test src/AgenticHarness.slnx
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-domain-models
section-02-application-interfaces
section-03-plan-validation
section-04-ef-core-persistence
section-05-plan-state-store
section-06-capability-model
section-07-process-sandbox
section-08-docker-sandbox
section-09-attestation
section-10-step-executors
section-11-plan-executor
section-12-plan-generator
section-13-cqrs-commands
section-14-agui-events
section-15-di-registration
section-16-configuration
END_MANIFEST -->

# Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable With |
|---------|------------|--------|---------------------|
| section-01-domain-models | - | 02, 03, 04, 06, 07, 08, 09 | - |
| section-02-application-interfaces | 01 | 05, 06, 07, 08, 09, 10, 11, 12, 13, 14 | - |
| section-03-plan-validation | 01, 02 | 11, 13 | 04, 05, 06, 07, 08, 09 |
| section-04-ef-core-persistence | 01 | 05 | 03, 06, 07, 08, 09 |
| section-05-plan-state-store | 02, 04 | 11 | 03, 06, 07, 08, 09 |
| section-06-capability-model | 01, 02 | 07, 08, 10 | 03, 04, 05, 09 |
| section-07-process-sandbox | 01, 02, 06 | 10 | 08, 09 |
| section-08-docker-sandbox | 01, 02, 06 | 10 | 07, 09 |
| section-09-attestation | 01, 02 | 10 | 03, 04, 05, 06, 07, 08 |
| section-10-step-executors | 02, 06, 07, 08, 09 | 11 | - |
| section-11-plan-executor | 02, 03, 05, 10 | 13 | 12 |
| section-12-plan-generator | 02 | 13 | 11 |
| section-13-cqrs-commands | 03, 11, 12 | 15 | 14 |
| section-14-agui-events | 02 | 15 | 13 |
| section-15-di-registration | 13, 14 | 16 | - |
| section-16-configuration | 15 | - | - |

## Execution Order (Batches)

1. **Batch 1**: section-01-domain-models (foundation, no dependencies)
2. **Batch 2**: section-02-application-interfaces (depends on 01)
3. **Batch 3**: section-03-plan-validation, section-04-ef-core-persistence, section-06-capability-model, section-09-attestation (parallel — all depend on 01+02 but not each other)
4. **Batch 4**: section-05-plan-state-store, section-07-process-sandbox, section-08-docker-sandbox (parallel — 05 needs 04, 07/08 need 06)
5. **Batch 5**: section-10-step-executors (needs 07, 08, 09)
6. **Batch 6**: section-11-plan-executor, section-12-plan-generator (parallel — independent)
7. **Batch 7**: section-13-cqrs-commands, section-14-agui-events (parallel — independent)
8. **Batch 8**: section-15-di-registration (wires everything)
9. **Batch 9**: section-16-configuration (final)

## Section Summaries

### section-01-domain-models
All domain types: PlanGraph, PlanStep, PlanEdge, StepType, StepConfiguration (abstract + 5 subtypes), PlanExecutionState, RetryPolicy, PlanConfiguration, ToolCapability, ToolPermissionProfile, SandboxIsolationLevel, ToolCapabilityAttribute, ToolExecutionAttestation, ResourceLimits. Plus unit tests for serialization, immutability, flags operations.

### section-02-application-interfaces
All interfaces: IPlanExecutor, IPlanStepExecutor, IPlanValidator, IPlanStateStore, IPlanGenerator, IPlanProgressNotifier, ISandboxExecutor, IProcessResourceLimiter, ICapabilityEnforcer, IAttestationService, IAttestationStore. No implementations — just contracts.

### section-03-plan-validation
PlanValidator implementing IPlanValidator with all 7 checks: cycle detection (Kahn's), unreachable nodes, zero root nodes, edge referential integrity, conditional branch completeness, self-referencing sub-plans, step config validation. FluentValidation validators per StepConfiguration subtype. Plus comprehensive test suite.

### section-04-ef-core-persistence
PlannerDbContext with entity configurations (PlanGraphEntity, PlanStepEntity, PlanEdgeEntity, StepExecutionStateEntity, PlanExecutionLogEntity). RowVersion concurrency tokens. JSON column handling for polymorphic StepConfiguration. Code-first migrations. NuGet package additions.

### section-05-plan-state-store
EfCorePlanStateStore implementing IPlanStateStore. Save/load/update/checkpoint/resume operations. Optimistic concurrency handling. IDbContextFactory usage for singleton callers.

### section-06-capability-model
Extend existing ToolPermissionBehavior with capability-based checks. ToolPermissionProfile resolution from attribute + appsettings. Deny-overrides-allow logic. Governance integration distinguishing capability checks vs. autonomy checks.

### section-07-process-sandbox
ProcessSandboxExecutor with stdin/stdout JSON protocol. WindowsJobObjectManager with Win32 P/Invoke (behind IProcessResourceLimiter). Hard kill via Job Object + Process.Kill backstop. Cross-platform runtime detection with Linux warning fallback.

### section-08-docker-sandbox
DockerSandboxExecutor via Docker.DotNet SDK. Container lifecycle management. Security enforcement: refuse execution when tool declares MinimumIsolation=Container and Docker unavailable. Network, memory, CPU, filesystem isolation.

### section-09-attestation
HmacAttestationService for signing/verification. Failure attestation support. Key rotation with versioned keychain. Key sourced from User Secrets / Key Vault.

### section-10-step-executors
All 5 keyed IPlanStepExecutor implementations: LlmCallStepExecutor (delegates to RunConversationCommand), ToolUseStepExecutor (routes through sandbox), HumanGateStepExecutor (QueueEscalationAsync, non-blocking), ConditionalBranchStepExecutor (reuses DecisionRule), SubPlanStepExecutor (isolated scope, depth limit).

### section-11-plan-executor
PlanExecutor with dynamic ready-queue scheduling. SemaphoreSlim bounded concurrency. Keyed SemaphoreSlim for per-plan serialization. Checkpoint/resume. Blocked step polling. Notifications via IPlanProgressNotifier.

### section-12-plan-generator
LlmPlanGeneratorService implementing IPlanGenerator. LLM generates structured JSON plan from task description. Schema-guided output. Validation before persistence.

### section-13-cqrs-commands
All MediatR commands/queries: GeneratePlanCommand, CreatePlanCommand, ExecutePlanCommand, CancelPlanCommand, RetryPlanStepCommand, GetPlanQuery, GetPlanHistoryQuery, ListPlansQuery. FluentValidation validators. Result<T> returns.

### section-14-agui-events
AgUiPlanProgressNotifier implementing IPlanProgressNotifier. New polymorphic AgUiEvent subtypes: PlanStartedEvent, PlanStepStartedEvent, PlanStepCompletedEvent, PlanStateUpdateEvent, SandboxStatusEvent, PlanCompletedEvent, PlanFailedEvent. JSON Patch for STATE_DELTA.

### section-15-di-registration
Wire all services in DependencyInjection.cs across layers. Keyed DI for step executors and sandbox executors. EF Core DbContext + DbContextFactory. MediatR pipeline behavior ordering. Layer registration order.

### section-16-configuration
PlannerOptions and SandboxOptions strongly-typed config. appsettings.json sections. IOptionsMonitor binding. Default values. Tool override configuration.
