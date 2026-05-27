# Phase 4 Interview Transcript

## Q1: Planner Architecture — Relationship to Existing Orchestration

**Question:** Should the planner replace, wrap, or run parallel to RunOrchestratedTaskCommandHandler? Should it target MAF WorkflowBuilder or build a custom graph engine?

**Answer:** Extend existing orchestration. Add DAG capabilities into/around the existing RunOrchestratedTaskCommandHandler.

## Q2: Sandbox Scope — Isolation Tiers

**Question:** Should Phase 4 implement both tiers (Process+JobObjects and Docker containers), or just the process tier?

**Answer:** Both tiers. Process+JobObjects as default for trusted tools, Docker.DotNet for elevated isolation. Full capability model.

## Q3: Plan Step Types

**Question:** Are the 4 listed types (LLM call, tool use, human gate, conditional branch) sufficient, or are additional types needed?

**Answer:** Add sub-plan invocation. Plans should be able to invoke other plans as steps (hierarchical composition). Total: 5 step types.

## Q4: Task State Persistence

**Question:** What persistence target for plan state? In-memory+JSONL (matching Phase 3), SQLite via EF Core, or in-memory only?

**Answer:** SQLite via EF Core. More robust querying, transactional state updates, better audit trails.

## Q5: Capability Model Declaration

**Question:** How should tool capabilities be declared? Attribute decoration, appsettings.json, or both?

**Answer:** Attribute + appsettings override. Tools declare defaults via [ToolCapability] attribute; appsettings can override/restrict per environment.

## Q6: Execution Attestation

**Question:** Include HMAC-signed attestation in Phase 4, defer, or hash-only?

**Answer:** Include in Phase 4. Tool output integrity matters for agent reasoning trust. Build it now with full HMAC signing.

## Q7: AG-UI Event Integration

**Question:** Extend existing AgUiEvents with plan-specific subtypes, create a separate channel, or use only standard AG-UI types?

**Answer:** Extend existing AgUiEvents. Add PlanStepStarted, PlanStepCompleted, PlanStateSnapshot, SandboxStatusEvent as new subtypes of AgUiEvent.

## Q8: Governance — Escalation Blocking Behavior

**Question:** When a plan step requires escalation, should the entire plan block, or should independent parallel branches continue?

**Answer:** Continue parallel branches. Only block the dependent subgraph. Independent branches keep executing.

## Q9: Scale Expectations

**Question:** How many concurrent plans and steps per plan?

**Answer:** Medium scale. 10-50 concurrent plan executions, 10-50 steps per plan.

## Q10: Sub-Plan Context Isolation

**Question:** Should child plans share the parent's execution context, or get their own isolated context? Wait or fire-and-forget?

**Answer:** Isolated context, always wait. Child plan gets own sandbox scope and escalation context. Parent step blocks until child completes.

## Q11: Timeout Defaults

**Question:** What are reasonable default timeouts? Should there be plan-level timeouts in addition to step-level?

**Answer:** 60s per step, 30min per plan defaults. Both configurable. Generous to allow for LLM calls and complex tool operations.
