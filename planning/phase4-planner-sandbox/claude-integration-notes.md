# Integration Notes: Opus Review Feedback

## Issues Integrated (updating claude-plan.md)

### #1 EF Core Lifetime Mismatches — INTEGRATE
Valid concern. EF Core is new to this codebase. The plan must address: migration strategy, scoped DbContext consumed by scoped services only, `IDbContextFactory<T>` for singleton callers (drift, learnings bridging). Document as precedent-setting architectural decision.

### #2 IToolExecutionCommand Doesn't Exist — INTEGRATE
Correct. The plan should reference the existing `IToolRequest` marker interface and extend it, not introduce a new one. `ToolCapabilityBehavior` should intercept `IToolRequest` implementations.

### #3 MediatR Pipeline Order Wrong — INTEGRATE
Valid. The plan incorrectly simplified the pipeline order. The actual pipeline has two registration sites (AI.Common and Common), and `ToolPermissionBehavior` + `GovernancePolicyBehavior` already exist. `ToolCapabilityBehavior` should either extend `ToolPermissionBehavior` or replace it with a superset. Must document relationship to existing behaviors.

### #4 IAutonomyTierResolver Signature Mismatch — INTEGRATE
Valid. Plan steps don't carry `SubagentType` or `SubagentDefinition`. Add a new overload `Resolve(PlanStepContext)` or map step types to `SubagentType` equivalents.

### #5 Expression Evaluator Security + DecisionRule Overlap — INTEGRATE
Valid on both counts. Reuse existing `DecisionRule` evaluation, add input sanitization, document security boundary.

### #7 Docker Fallback Security Downgrade — INTEGRATE
Critical insight. If a tool declares `MinimumIsolation = Container` and Docker is unavailable, execution MUST be refused, not downgraded. Fallback only for tools without explicit minimum isolation.

### #9 Sub-Plan Recursion Depth — INTEGRATE
Valid. Add `MaxSubPlanDepth` (default: 5) to `PlanConfiguration`.

### #10 Plan Validation Gaps — INTEGRATE
All four missing validations are valid: self-referencing sub-plans, edge referential integrity, conditional branch completeness, zero root nodes.

### #11 StepConfiguration Polymorphism — INTEGRATE
Valid. Must specify as abstract record in Domain.AI/Planner/ with JSON column persistence and explicit discriminator handling.

### #12 SQLite Concurrency — INTEGRATE
Correct that MediatR isn't single-threaded per plan. Add optimistic concurrency via EF Core row version tokens on PlanGraphEntity and StepExecutionStateEntity.

### #14 Scheduling Bug (Layers vs. Dynamic Queue) — INTEGRATE
Valid observation. Switch to dynamic ready-queue model: maintain a concurrent queue of ready steps, whenever a step completes check what it unblocks and enqueue. Drop the layer abstraction.

### #15 Missing Plan Generation Pathway — INTEGRATE
Critical gap. Add an `LlmPlanGeneratorService` that extends the existing orchestrator's decomposition to produce `PlanGraph` objects. The LLM generates structured JSON, validated against the plan schema.

### #16 AG-UI Clean Architecture Violation — INTEGRATE
Correct. Follow existing notification pattern: define `IPlanProgressNotifier` in Application, implement in Presentation with AG-UI events. Executors call the Application interface, not Presentation types.

### #19 Attestation on Crashed Processes — INTEGRATE
Valid. Make `Attestation` nullable on `SandboxExecutionResult`. Define a `FailedAttestation` record with input hash + error description (no output hash).

### #20 HumanGate Blocking Contradiction — INTEGRATE
Correct contradiction. Use `QueueEscalationAsync` (non-blocking), transition step to `Blocked`, plan executor polls for resolution on each scheduling pass.

## Issues NOT Integrated (with reasoning)

### #6 Windows Job Objects Cross-Platform — PARTIAL
The concern is valid but the solution is already in the plan: `IProcessResourceLimiter` abstraction. Phase 4 implements Windows only. On Linux, the process sandbox skips resource limits with a logged warning (tools still execute, just without OS-level caps). Container isolation IS the cross-platform answer. Adding to plan: explicit Linux behavior documentation and `IProcessResourceLimiter` in the layer mapping.

### #8 HMAC Key Management — PARTIAL
Valid security concern, but the fix is straightforward: specify User Secrets (dev) / Key Vault (prod) per existing patterns. Key rotation: store key version in attestation, keep old keys for verification window. Not a plan architecture issue — implementation detail.

### #13 RunOrchestratedTaskCommandHandler Integration Vagueness — PARTIAL
The reviewer wants more specificity. Clarify: `ExecutePlanCommand` is a NEW entry point that coexists with `RunOrchestratedTaskCommand`. The existing handler is NOT modified. `LlmCallStepExecutor` delegates to the existing `RunConversationCommand` via MediatR (same pattern the orchestrator uses). Sub-plan uses the same `IServiceScopeFactory` pattern intentionally.

### #17 File Count / PR Split — NOT INTEGRATING
This is execution planning, not architecture. The deep-implement phase will handle PR splitting. The plan itself shouldn't dictate PR boundaries.

### #18 Test Strategy — PARTIAL
TDD is handled by the separate `claude-plan-tdd.md` step (Step 16). The plan's test section is intentionally brief because TDD planning is a dedicated workflow step. Will ensure TDD plan covers the reviewer's specific concerns (P/Invoke testing, in-memory SQLite limitations).
