# Opus Review

**Model:** claude-opus-4
**Generated:** 2026-05-15T13:00:00Z

---

## Plan Review: Phase 4 -- Planner & Code Sandbox

### 1. EF Core Is a New Dependency -- This Is a Bigger Deal Than the Plan Acknowledges

The entire existing codebase uses zero EF Core. There are no `DbContext` classes anywhere -- persistence is file-based (JSONL files, JSON checkpoints, filesystem stores). Introducing `PlannerDbContext` with EF Core + SQLite means:

- A new NuGet dependency (`Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` for tooling) added to `Infrastructure.AI`.
- The `Infrastructure.AI` project doesn't currently reference any EF Core packages. This will pull in a significant dependency tree.
- Migration infrastructure (`dotnet ef`) needs to be established for the first time in this project.
- The plan says "Code-first EF Core migrations" but doesn't address how migrations get applied (at startup? manual CLI? Both?). For a template project that consumers clone, this matters -- they need a clear story.
- Every other persistence store in the project is `IDisposable`-friendly singletons (JSONL append, in-memory). EF Core `DbContext` is scoped, which creates a lifetime mismatch with many of the services registered as singletons in `DependencyInjection.cs` (line 86-260). The plan registers `IPlanExecutor` as scoped, which is correct, but doesn't address how singleton services (like `IDriftDetectionService`, which the plan feeds data to in Section 1.7) will access the scoped `PlannerDbContext`. You'll hit "Cannot consume scoped service from singleton" errors.

**Recommendation**: Either accept EF Core as a first-class project dependency and document the precedent being set, or use the existing file-based persistence pattern (JSONL + JSON) for consistency. If EF Core, explicitly document the migration application strategy and address the singleton/scoped lifetime boundary.

### 2. `IToolExecutionCommand` Does Not Exist

Section 2.2 says `ToolCapabilityBehavior` intercepts `IToolExecutionCommand` requests. Searching the codebase: this interface does not exist. The existing tool permission system uses `IToolRequest` (at `Application.AI.Common/Interfaces/MediatR/IToolRequest.cs`). The plan introduces a new marker interface without acknowledging the existing one or explaining the relationship.

**Question**: Is `IToolExecutionCommand` meant to extend `IToolRequest`? Replace it? Be a separate concern? This needs explicit design rationale.

### 3. MediatR Pipeline Order Is Wrong

The plan (Section "MediatR Pipeline Order") claims the order is:

```
UnhandledException -> AuditTrail -> AgentContextPropagation -> RequestValidation 
  -> Authorization -> ToolCapability -> RequestTracing -> Caching -> Handler
```

The actual order in the codebase is (from `Application.AI.Common/DependencyInjection.cs:58-68` and `Application.Common/DependencyInjection.cs:62-67`):

AI layer (outermost): `UnhandledException -> AgentContextPropagation -> AuditTrail -> ContentSafety -> ToolPermission -> GovernancePolicy -> PromptInjection -> Hook -> RetrievalAudit -> ResponseSanitization`

Common layer (inner): `RequestValidation -> Authorization -> Caching -> RequestTracing -> Timeout`

The plan's ordering doesn't match reality. It also says to insert `ToolCapabilityBehavior` "between `AuthorizationBehavior` and `RequestTracingBehavior`" -- but those are in `Application.Common`, while `ToolCapabilityBehavior` would logically live in `Application.AI.Common` near `ToolPermissionBehavior` and `GovernancePolicyBehavior`. The plan conflates the two DI registration sites.

Furthermore, there's already a `ToolPermissionBehavior` AND a `GovernancePolicyBehavior` doing capability/permission checks. The plan doesn't explain how `ToolCapabilityBehavior` differs from or relates to these existing behaviors.

### 4. `IAutonomyTierResolver.Resolve()` Signature Mismatch

Section 1.7 says "the plan executor calls `IAutonomyTierResolver.Resolve()` with the step's context." The actual interface takes either a `SubagentType` enum or a `SubagentDefinition`. A plan step doesn't carry either of these. The plan needs to either:

- Extend `IAutonomyTierResolver` with a new overload
- Add `SubagentType` or `SubagentDefinition` context to plan steps
- Define a new resolution mechanism

This is not a minor detail -- it's the integration point between the planner and the governance system.

### 5. ConditionalBranch Expression Evaluator Is Underspecified and Risky

Section 1.4 says the `ConditionalBranchStepExecutor` "uses a simple expression evaluator (not a full scripting engine -- JSON path comparisons, boolean logic, null checks)." The project already has `DecisionRule` (at `Domain.Common/Workflow/DecisionRule.cs`) with condition expressions using "C# expression syntax with AND, OR, comparison operators, and parentheses."

Two problems:
- The plan doesn't reference this existing pattern. Is the planner's expression evaluator the same thing? Different? Should they share code?
- An expression evaluator that takes user-provided strings and evaluates them is a security concern. Even "simple" evaluators can be vulnerable to injection if not carefully sandboxed. The plan says nothing about validation/sanitization of condition expressions.

### 6. Windows Job Objects -- Cross-Platform Story Is Inadequate

The plan says "wrap it behind an `IProcessResourceLimiter` interface so a cgroups-based Linux implementation can be swapped in later. Phase 4 only implements Windows." This is a template project. Template consumers may deploy to Linux containers (common for .NET workloads). The plan needs:

- What happens when `ProcessSandboxExecutor` runs on Linux? Does it silently skip resource limits? Throw? Fall back to Docker?
- The `IProcessResourceLimiter` interface is mentioned in the narrative but doesn't appear in the layer mapping (Section "Architecture Overview"). Where does it live?
- P/Invoke for Win32 Job Objects adds platform-specific native interop to what is currently a pure managed codebase. This should be explicitly called out as an architectural decision.

### 7. Docker Sandbox -- Unaddressed Failure Modes

Section 2.5 says "If Docker is not available, container isolation level is rejected with a clear error message, and tools requiring it fall back to process isolation with a warning." But Section 2.7 says isolation level is determined by combining attribute, autonomy tier, and config. If a tool declares `MinimumIsolation = Container` (via attribute), and Docker isn't available, does the fallback to process isolation violate the tool's own declared minimum? That's a security downgrade -- the tool author explicitly said it needs container isolation.

The plan should define whether fallback is permitted when the tool's own declaration requests higher isolation, or if execution should be refused outright.

### 8. HMAC Attestation Key Management Is Weak

Section 2.6 says "derive a unique key from the plan ID + a server-side secret." If the server-side secret is in `IConfiguration` (appsettings), it's likely in plaintext on disk. The plan should:

- Specify that the secret must come from User Secrets / Key Vault (per the project's existing security rules in `.claude/rules/security.md`)
- Address key rotation -- if the secret changes, all existing attestations become unverifiable. Is there a migration path?
- The session key derivation is plan-scoped, but attestations are persisted. What happens when you verify attestations from old plans after the secret rotates?
- "Hash the tool input with SHA-256" -- tool inputs may contain sensitive data. Storing SHA-256 hashes in the database is fine for integrity, but the signing payload (`ToolName|InputHash|OutputHash|Timestamp`) is deterministic. This is stated behavior, not a bug, but worth noting that anyone with the signing secret can forge attestations. HMAC is tamper detection, not a cryptographic proof of execution.

### 9. Sub-Plan Invocation Creates Unbounded Recursion Risk

Section 1.4 (`SubPlanStepExecutor`) says it "creates a new DI scope, instantiates a new `IPlanExecutor`, and awaits the child plan's completion." There's no stated depth limit. A plan with `SubPlanInvocation` steps pointing to plans that themselves contain `SubPlanInvocation` steps can recurse indefinitely. 

`PlanConfiguration` has `MaxParallelSteps` and `PlanTimeout` but no `MaxSubPlanDepth`. The plan needs an explicit depth limit, either in `PlanConfiguration` or as a global setting.

### 10. Plan Validation -- Cycle Detection Is Necessary but Insufficient

Section 1.2 validates cycles and unreachable nodes. Missing validations:

- **Self-referencing sub-plans**: A plan that invokes itself as a sub-plan. Kahn's algorithm won't catch this because the cycle is across plan boundaries, not within a single plan's edges.
- **Missing step references**: What if an edge references a `PlanStepId` that doesn't exist in the `Steps` collection? The plan doesn't mention this basic referential integrity check.
- **ConditionalBranch completeness**: A `ConditionalBranch` step must have both `ConditionalTrue` and `ConditionalFalse` outgoing edges. If one is missing, execution can dead-end.
- **Root node validation**: The plan says "BFS/DFS from all root nodes (nodes with no incoming edges)." What if there are zero root nodes? (This would imply a cycle, but the error message should be distinct.)

### 11. `StepConfiguration` Polymorphism -- Missing from Architecture

The plan says `StepConfiguration` uses `JsonPolymorphicAttribute` but doesn't list it in the Domain model folder or define the interface/base class. This is the most complex type in the system (five subtypes with different validation rules) and it's hand-waved. The plan should specify:

- Is `StepConfiguration` a `record`? `abstract class`? 
- Where exactly does it live? (`Domain.AI/Planner/` presumably, but not stated)
- How does EF Core persist polymorphic `StepConfiguration` in `PlanStepEntity`? JSON column? Separate tables? The plan says "config JSON" for the entity, which implies JSON serialization, but polymorphic JSON deserialization needs the discriminator to round-trip correctly through EF Core. This has gotchas with System.Text.Json.

### 12. SQLite WAL + Single-Writer Claim Is Misleading

Section 1.6 says "Write serialization via the single-writer pattern (all writes go through MediatR handlers which are single-threaded per plan execution)." MediatR handlers are NOT single-threaded per plan execution. MediatR dispatches to whatever thread the ASP.NET Core request pipeline puts it on. If two HTTP requests both send `ExecutePlanCommand` for the same plan, you get concurrent writes.

The plan needs actual concurrency control:

- Optimistic concurrency via EF Core row version / concurrency token
- A `SemaphoreSlim` keyed by plan ID
- Or explicit documentation that a plan can only be executed by one handler at a time, with enforcement

### 13. Existing `RunOrchestratedTaskCommandHandler` Integration Is Vague

Section "Key Design Decisions" says "The planner adds DAG capabilities around `RunOrchestratedTaskCommandHandler`, not replacing it." But `RunOrchestratedTaskCommandHandler` has its own subtask decomposition, sequential delegation, and synthesis loop. The plan says the existing handler "becomes one possible plan step executor" but doesn't explain:

- How does the `LlmCallStepExecutor` relate to `RunConversationCommand` which the existing handler delegates to?
- Does `ExecutePlanCommand` replace `RunOrchestratedTaskCommand` for new workloads, or do they coexist with different entry points?
- The existing handler uses `_scopeFactory.CreateAsyncScope()` for sub-agent isolation. The plan's `SubPlanStepExecutor` does the same thing. Is this coincidence or intentional reuse?

### 14. `PlanExecutor` Algorithm Has a Scheduling Bug

Section 1.3 says:
```
3. For each layer (in order):
   a. Filter to steps in Ready status
   ...
   f. Recalculate ready steps (a completed step may unblock steps in the same or later layers)
```

Topological sort produces dependency layers, but step 3f says to recalculate after each step completion. If you're iterating layers in order AND recalculating within a layer, you have a confused scheduling model. Either:

- Use layers strictly (process all ready steps in layer N, wait, then move to layer N+1) -- simpler but less parallel
- Use a dynamic ready-queue model (whenever a step completes, check what it unblocks) -- more parallel but not layer-based

The plan tries to do both. The "recalculate" within step 3f breaks the layer abstraction. Pick one model and be explicit.

### 15. Missing: How Does an LLM Actually Create a Plan?

The plan defines the executor, state machine, and persistence, but never addresses how a `PlanGraph` gets created in the first place. `CreatePlanCommand` "validates and persists a new plan graph," but who constructs the `PlanGraph` object? Options:

- An LLM generates a plan as structured JSON output (needs schema guidance, prompt engineering)
- A human constructs it via API
- The orchestrator decomposes a task into a DAG automatically

Without this, the planner is a beautifully engineered engine with no intake funnel. The existing `RunOrchestratedTaskCommandHandler` has this (it asks the LLM to decompose). The plan should specify the plan generation pathway.

### 16. AG-UI Events Live in Presentation Layer but Domain/Application Needs to Emit Them

The plan says plan executors emit AG-UI events, but `AgUiEvent` types and `AgUiEventWriter` live in `Presentation.AgentHub`. The executors live in `Infrastructure.AI`. This creates a dependency from Infrastructure to Presentation, which violates Clean Architecture.

The existing codebase solves this with notification interfaces (`AgUiDriftNotifier`, `AgUiEscalationNotifier`, `AgUiLearningNotifier` in the Presentation layer implement domain-facing notification interfaces from Application). The plan should follow this pattern: define planner notification interfaces in Application, implement them in Presentation with AG-UI events.

### 17. File Count and Size Concerns

This plan calls for at minimum:
- ~8 domain model files
- ~8 application interface files  
- ~5 validator files
- ~5 EF entity/config files + DbContext + migration
- ~7 infrastructure implementation files
- ~7 CQRS command/query + handler files
- ~7 AG-UI event type additions

That's ~47+ new files, many with non-trivial logic. The implementation order (15 phases) is likely 2-3 full implementation sessions. The plan doesn't discuss phasing within a single PR or whether this should be split into multiple PRs (e.g., Planner as PR#1, Sandbox as PR#2).

### 18. Test Strategy Is an Afterthought

"Integration tests -- Full plan execution with in-memory SQLite" is listed as step 15 of 16. The project requires 80% test coverage on new code. With 47+ files, that's a significant test effort. The plan should:

- Identify which components need unit tests vs. integration tests
- Define test boundary (mock the sandbox executor for plan executor tests? or real process execution?)
- Account for testing Windows Job Object P/Invoke code (this can't run in CI on Linux containers)
- Address how `PlannerDbContext` is tested (in-memory SQLite has behavioral differences from file SQLite, especially around WAL mode and concurrent access)

### 19. `SandboxExecutionResult.Attestation` Is Required but Process May Crash

`SandboxExecutionResult` has `required ToolExecutionAttestation Attestation`. If the sandboxed process crashes (OOM kill, timeout, segfault), you can't compute `OutputHash` because there's no output. The attestation model assumes successful output always exists. The plan should define how attestations work for failed/crashed executions -- either make `Attestation` nullable or define a sentinel attestation for failure cases.

### 20. `HumanGateStepExecutor` Calls Blocking `RequestEscalationAsync` -- Contradicts Non-Blocking Design

Section 1.4 says `HumanGateStepExecutor` "calls `IEscalationService.RequestEscalationAsync` (blocking mode)." But Section 1.3 says "the executor does NOT wait" when a step enters `Blocked` status. These contradict. If the executor blocks on `RequestEscalationAsync`, the thread is consumed until the human responds. If the plan executor doesn't wait on blocked steps, then the HumanGate executor should use `QueueEscalationAsync` (non-blocking) and transition the step to `Blocked`.

The existing `IEscalationService` already distinguishes these two modes. The plan should use `QueueEscalationAsync` for the HumanGate executor and have the plan executor poll/react to escalation resolution.

---

### Summary of Critical Issues

| # | Severity | Issue |
|---|----------|-------|
| 1 | HIGH | EF Core is entirely new dependency; lifetime mismatches with existing singletons |
| 2 | HIGH | `IToolExecutionCommand` doesn't exist; relationship to `IToolRequest` undefined |
| 3 | HIGH | MediatR pipeline order doesn't match actual codebase; duplicate capability checking unclear |
| 4 | MEDIUM | `IAutonomyTierResolver` signature mismatch with plan step context |
| 5 | MEDIUM | Expression evaluator security + overlap with existing `DecisionRule` |
| 6 | MEDIUM | Windows-only P/Invoke with no Linux story for a template project |
| 7 | HIGH | Docker fallback can silently downgrade security from tool author's declared minimum |
| 8 | MEDIUM | HMAC key in plaintext config violates project security rules |
| 9 | MEDIUM | Unbounded sub-plan recursion depth |
| 12 | HIGH | SQLite concurrency claim is wrong; no actual concurrency control |
| 16 | HIGH | Clean Architecture violation: Infrastructure emitting Presentation-layer events |
| 20 | HIGH | HumanGate blocking vs. non-blocking escalation is contradictory |
