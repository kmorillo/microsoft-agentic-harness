# Section 01: Domain Models -- Code Review

**Reviewer:** claude-code-reviewer
**Date:** 2026-05-15
**Verdict:** WARNING -- No CRITICAL or HIGH issues. MEDIUM issues require attention before merge.

---

## Summary

25 implementation files + 5 test files reviewed. Pure domain models (records, enums, value objects) for the Planner+Sandbox subsystem. Overall quality is high -- immutability patterns are correct, XML docs are comprehensive, naming follows project conventions, and JSON polymorphic serialization is properly configured.

---

## Spec Compliance

### Files: All 25 implementation files present and matching spec

| Spec File | Status | Notes |
|-----------|--------|-------|
| Planner/PlanId.cs | PASS | readonly record struct, New() factory |
| Planner/PlanStepId.cs | PASS | Same pattern |
| Planner/StepType.cs | PASS | 5 members match spec |
| Planner/EdgeType.cs | PASS | 4 members match spec |
| Planner/StepExecutionStatus.cs | PASS | 7 members match spec |
| Planner/BackoffStrategy.cs | PASS | 3 members |
| Planner/ErrorRecovery.cs | PASS | 4 members |
| Planner/RetryPolicy.cs | PASS | Defaults match spec (3/1s/Exponential/FailStep) |
| Planner/PlanConfiguration.cs | PASS | Defaults match spec (30m/10/5) |
| Planner/PlanEdge.cs | PASS | Positional record params (equivalent to required init) |
| Planner/StepConfiguration.cs | PASS | Polymorphic attributes, 5 derived types |
| Planner/LlmCallConfig.cs | PASS | All 4 props, defaults match |
| Planner/ToolUseConfig.cs | PASS | Cross-folder ref to Sandbox |
| Planner/HumanGateConfig.cs | PASS | 3 props, 1hr default |
| Planner/ConditionalBranchConfig.cs | PASS | 3 required props |
| Planner/SubPlanConfig.cs | PASS | Recursive PlanGraph reference, IsolateContext default true |
| Planner/PlanStep.cs | PASS | Cross-ref to AutonomyLevel confirmed exists |
| Planner/PlanGraph.cs | PASS | IReadOnlyList collections |
| Planner/StepExecutionState.cs | PASS | Cross-ref to ToolExecutionAttestation |
| Sandbox/ToolCapability.cs | PASS | [Flags], 8 members, correct bit shifts |
| Sandbox/SandboxIsolationLevel.cs | PASS | Explicit values 0/1/2, ordering documented |
| Sandbox/ToolPermissionProfile.cs | PASS | 7 props, deny lists included |
| Sandbox/ToolCapabilityAttribute.cs | PASS | Constructor + init property pattern |
| Sandbox/ResourceLimits.cs | PASS | 4 props, byte values match spec |
| Attestation/ToolExecutionAttestation.cs | PASS | 8 props, required/optional correctly applied |

### Tests: All 5 test files present. 1 specified test case MISSING.

| Spec Test | Status | Notes |
|-----------|--------|-------|
| PlanGraphTests.cs (7 tests) | 6/7 | **Missing: PlanStep_RequiredFields_CannotBeNull** |
| StepConfigurationTests.cs (2 tests) | 2/2 | PASS |
| RetryPolicyTests.cs (1 test) | 1/1 | PASS |
| ToolCapabilityTests.cs (5 tests) | 5/5 | PASS |
| ToolExecutionAttestationTests.cs (3 tests) | 3/3 | PASS |
---

## Issues

### [MEDIUM] Missing test: PlanStep_RequiredFields_CannotBeNull
**File:** src/Content/Tests/Domain.AI.Tests/Planner/PlanGraphTests.cs
**Issue:** The section spec defines a test PlanStep_RequiredFields_CannotBeNull that validates required init properties cannot be null. This test is absent from the implementation.
**Fix:** Add the missing test. Note that the required keyword is a compile-time constraint, so the test validates the positive case (all required props are set and non-null).

---

### [MEDIUM] HumanGateConfig.ApprovalStrategy is stringly-typed
**File:** src/Content/Domain/Domain.AI/Planner/HumanGateConfig.cs:16
**Issue:** ApprovalStrategy is a string with valid values AnyOf, AllOf, Quorum documented only in XML docs. This is a domain model layer -- the natural choice would be an enum (like BackoffStrategy and ErrorRecovery). A string invites typos and makes validation harder in section-03.
**Recommendation:** Matches spec as written. Flag for the spec author: an ApprovalStrategy enum would be more consistent with the other domain enums. If the intent is extensibility (new strategies without enum changes), the string is defensible.
**Verdict:** No code change required now. Consider introducing an enum in a follow-up.

---

### [MEDIUM] ToolUseConfig.InputParameters default uses mutable backing type
**File:** src/Content/Domain/Domain.AI/Planner/ToolUseConfig.cs:13-14
**Issue:** Default value is new Dictionary. While the property type is IReadOnlyDictionary (correct), the backing instance IS a mutable Dictionary which can be downcast and mutated. A consumer could cast and inject unexpected keys.
**Fix:** Use ImmutableDictionary<string, object?>.Empty (preferred, .NET 8+) or wrap in ReadOnlyDictionary.
**Severity note:** For internal code the risk is low, but this is a template project teaching immutability patterns -- using a truly immutable type would be more instructive.

---

### [LOW] PlanEdge uses positional record parameters instead of init-only properties
**File:** src/Content/Domain/Domain.AI/Planner/PlanEdge.cs:8-12
**Issue:** PlanEdge is the only record in the section using positional constructor syntax; all others use required init-only properties. Positional params are semantically equivalent (they generate init-only properties), but the style inconsistency is notable.
**Verdict:** Functionally correct. Positional records are more concise for small value types (4 fields). Mention for awareness only.

---

### [LOW] Test assertions use standard xUnit Assert (not FluentAssertions)
**File:** All 5 test files
**Issue:** The test project has FluentAssertions as a dependency, but all tests use standard Assert calls. The spec says standard xUnit asserts so this matches spec.
**Verdict:** Matches spec. No change required.

---

### [LOW] Serialization round-trip test does not verify property preservation
**File:** src/Content/Tests/Domain.AI.Tests/Planner/StepConfigurationTests.cs:32-70
**Issue:** RoundTripsAllFiveSubtypes verifies type round-trips but only asserts type equality. The spec says all properties preserved but the test does not verify individual property values after deserialization.
**Fix:** Add property assertions for at least one subtype after deserialization. Since these models will be persisted via EF Core JSON columns (section-04), property preservation matters.

---

### [LOW] StepExecutionState.AttemptCount doc is slightly ambiguous
**File:** src/Content/Domain/Domain.AI/Planner/StepExecutionState.cs:17
**Issue:** XML doc says Number of execution attempts made (including the initial attempt) but default is 0. Could confuse consumers into thinking AttemptCount=1 means one retry when it means first try.
**Verdict:** No code change needed. Doc could be clarified to Number of execution attempts made. Starts at 0 (not yet attempted).
---

## Security

No issues found:
- No hardcoded secrets, API keys, or tokens
- No SQL or injection vectors (pure domain models)
- No user-controlled file paths or input validation bypass
- HMAC attestation model correctly separates hash from signature
- KeyVersion field enables key rotation
- ConditionExpression documented as restricted to JSON path comparisons + boolean ops (enforcement in section-06)

---

## Immutability

All types correctly use:
- sealed record for concrete types
- readonly record struct for value-object IDs
- required keyword for mandatory properties
- init-only setters throughout
- IReadOnlyList<T> and IReadOnlyDictionary<K,V> for collections

One minor gap: ToolUseConfig.InputParameters default (see MEDIUM issue above).

---

## XML Documentation

All 25 public types have summary documentation. All properties have doc comments. Cross-references use see-cref correctly. Template consumers will find the documentation sufficient.

---

## Verdict

| Priority | Count | Details |
|----------|-------|---------|
| CRITICAL | 0 | -- |
| HIGH | 0 | -- |
| MEDIUM | 3 | Missing test, stringly-typed enum candidate, mutable backing dict |
| LOW | 4 | Style consistency, test assertion depth, positional record syntax, doc clarity |

**Recommendation:** WARNING -- approve with the following fixes before merge:
1. **Must fix:** Add the missing PlanStep_RequiredFields_CannotBeNull test (spec compliance)
2. **Should fix:** Use ImmutableDictionary for ToolUseConfig.InputParameters default (immutability teaching)
3. **Should fix:** Strengthen serialization round-trip test to verify property values (correctness)

Items 2 and 3 are should-fix for a template project. Item 1 is must-fix for spec compliance.