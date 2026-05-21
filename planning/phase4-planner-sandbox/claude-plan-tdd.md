# Phase 4 TDD Plan: Planner & Code Sandbox

Testing framework: xUnit, Moq, FluentAssertions. Integration tests use `WebApplicationFactory<Program>` + in-memory SQLite. Naming convention: `MethodName_Scenario_ExpectedResult`. All tests in corresponding `*.Tests` projects.

---

## 1. Domain Models

### Domain.AI.Tests/Planner/

```csharp
// Test: PlanId_NewId_GeneratesUniqueGuids
// Test: PlanId_Equality_SameGuidAreEqual
// Test: PlanStep_RequiredFields_CannotBeNull
// Test: StepConfiguration_JsonPolymorphic_RoundTripsAllFiveSubtypes
// Test: StepConfiguration_Discriminator_PreservesTypeInfoThroughSerialization
// Test: RetryPolicy_Defaults_ThreeRetriesExponentialBackoff
// Test: PlanConfiguration_MaxSubPlanDepth_DefaultsFive
// Test: PlanGraph_Steps_IsImmutableList
// Test: EdgeType_ConditionalTrue_HasDistinctValue
// Test: StepExecutionStatus_AllStates_CoverExpectedTransitions
```

### Domain.AI.Tests/Sandbox/

```csharp
// Test: ToolCapability_Flags_CanCombineMultiple
// Test: ToolCapability_BitwiseAnd_DetectsMissingCapabilities
// Test: ToolPermissionProfile_DeniedPaths_OverrideAllowedPaths
// Test: SandboxIsolationLevel_Ordering_ContainerHigherThanProcess
// Test: ToolCapabilityAttribute_OnClass_DeclaresCapabilitiesAndMinIsolation
```

### Domain.AI.Tests/Attestation/

```csharp
// Test: ToolExecutionAttestation_FailureAttestation_HasNullOutputHash
// Test: ToolExecutionAttestation_SuccessAttestation_HasBothHashes
// Test: ToolExecutionAttestation_KeyVersion_IsRequired
```

---

## 2. Application Interfaces

No tests for interfaces themselves — tested through implementations.

---

## 3. Plan Validation

### Application.Core.Tests/Validators/Planner/

```csharp
// Test: Validate_CyclicGraph_ReturnsFail (A->B->C->A)
// Test: Validate_AcyclicGraph_ReturnsSuccess
// Test: Validate_UnreachableNode_ReturnsFail (node with no path from root)
// Test: Validate_ZeroRootNodes_ReturnsFail (distinct from cycle error)
// Test: Validate_EdgeReferencesNonexistentStep_ReturnsFail
// Test: Validate_ConditionalBranch_MissingTrueEdge_ReturnsFail
// Test: Validate_ConditionalBranch_MissingFalseEdge_ReturnsFail
// Test: Validate_ConditionalBranch_BothEdgesPresent_ReturnsSuccess
// Test: Validate_SelfReferencingSubPlan_ReturnsFail (child plan ID == current plan ID)
// Test: Validate_AncestorReferencingSubPlan_ReturnsFail (child references grandparent)
// Test: Validate_LlmCallConfig_MissingDeploymentKey_ReturnsFail
// Test: Validate_ToolUseConfig_UnregisteredToolKey_ReturnsFail
// Test: Validate_HumanGateConfig_InvalidApprovalStrategy_ReturnsFail
// Test: Validate_ResourceEstimation_ReturnsCriticalPathDuration
// Test: Validate_EmptyGraph_ReturnsFail
// Test: Validate_SingleStepGraph_ReturnsSuccess
```

---

## 4. EF Core Persistence

### Infrastructure.AI.Tests/Persistence/

```csharp
// Test: PlannerDbContext_Migrate_CreatesAllTables
// Test: PlanGraphEntity_Insert_PersistsAllFields
// Test: PlanGraphEntity_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException (RowVersion)
// Test: PlanStepEntity_ConfigJson_RoundTripsPolymorphicConfig
// Test: PlanStepEntity_ConfigJson_PreservesDiscriminator
// Test: PlanEdgeEntity_ForeignKeys_EnforcedByDb
// Test: StepExecutionStateEntity_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException
// Test: StepExecutionStateEntity_AttestationJson_RoundTripsNullable
// Test: PlanExecutionLogEntity_AppendOnly_InsertsWithTimestamp
```

---

## 5. Plan State Store

### Infrastructure.AI.Tests/Planner/

```csharp
// Test: SavePlanAsync_NewPlan_PersistsGraphAndSteps
// Test: LoadPlanAsync_ExistingPlan_ReturnsCompleteGraph
// Test: LoadPlanAsync_NonexistentPlan_ReturnsNull
// Test: UpdateStepStateAsync_StatusTransition_PersistsNewState
// Test: UpdateStepStateAsync_ConcurrentUpdate_HandlesOptimisticConcurrency
// Test: GetExecutionHistoryAsync_MultipleSteps_ReturnsChronological
// Test: CheckpointAsync_MidExecution_SavesAllStepStates
// Test: ResumeAsync_FromCheckpoint_RebuildsReadyQueue
```

---

## 6. Capability Model

### Application.Core.Tests/Behaviors/

```csharp
// Test: ToolPermissionBehavior_AllCapabilitiesGranted_PassesThrough
// Test: ToolPermissionBehavior_MissingCapability_ReturnsFail
// Test: ToolPermissionBehavior_DeniedPath_ReturnsFail (even if AllowedPaths matches)
// Test: ToolPermissionBehavior_DeniedHost_ReturnsFail
// Test: ToolPermissionBehavior_AppsettingsOverride_RestrictsAttributeDefaults
// Test: ToolPermissionBehavior_AppsettingsOverride_CannotExpandBeyondAttribute
// Test: ToolPermissionProfile_Resolution_AttributeFallbackWhenNoOverride
// Test: ToolPermissionProfile_Resolution_OverrideTakesPrecedence
```

---

## 7. Process Sandbox

### Infrastructure.AI.Tests/Sandbox/

```csharp
// Test: ProcessSandboxExecutor_SuccessfulExecution_ReturnsOutputAndAttestation
// Test: ProcessSandboxExecutor_Timeout_KillsProcessAndReturnsFail
// Test: ProcessSandboxExecutor_ProcessCrash_ReturnsFailureAttestation
// Test: ProcessSandboxExecutor_StdinInput_SerializesAsJson
// Test: ProcessSandboxExecutor_WorkspaceCleanup_DeletesTempDir
// Test: WindowsJobObjectManager_CreateAndAssign_SetsResourceLimits (Windows-only, skip on Linux CI)
// Test: WindowsJobObjectManager_Dispose_ClosesJobHandle
// Test: WindowsJobObjectManager_MemoryLimit_EnforcedOnProcess (Windows-only)
// Test: ProcessSandboxExecutor_LinuxWithoutJobObjects_ExecutesWithWarning (Linux-only)
// Test: IProcessResourceLimiter_Interface_CanBeMocked
```

---

## 8. Docker Sandbox

### Infrastructure.AI.Tests/Sandbox/

```csharp
// Test: DockerSandboxExecutor_SuccessfulExecution_ReturnsOutputAndAttestation
// Test: DockerSandboxExecutor_Timeout_StopsContainer
// Test: DockerSandboxExecutor_NetworkNone_DefaultConfig
// Test: DockerSandboxExecutor_NetworkAccess_OverridesNetworkMode
// Test: DockerSandboxExecutor_MemoryLimit_PassedToHostConfig
// Test: DockerSandboxExecutor_ReadonlyRootfs_Enabled
// Test: DockerSandboxExecutor_AutoRemove_Enabled
// Test: DockerSandboxExecutor_DockerUnavailable_MinIsolationContainer_Refuses
// Test: DockerSandboxExecutor_DockerUnavailable_NoMinIsolation_FallsBack
// Test: DockerSandboxExecutor_WorkspaceMount_BindsCorrectly
```

Note: Docker tests require Docker daemon. Mark with `[Trait("Category", "Integration")]` and skip in CI if Docker unavailable.

---

## 9. Attestation Service

### Infrastructure.AI.Tests/Attestation/

```csharp
// Test: HmacAttestationService_Sign_ProducesValidSignature
// Test: HmacAttestationService_Verify_AcceptsValidSignature
// Test: HmacAttestationService_Verify_RejectsTamperedOutput
// Test: HmacAttestationService_Verify_RejectsTamperedInput
// Test: HmacAttestationService_Verify_RejectsTamperedTimestamp
// Test: HmacAttestationService_FailureAttestation_SignsWithNullOutputHash
// Test: HmacAttestationService_KeyRotation_OldKeyStillVerifies
// Test: HmacAttestationService_KeyRotation_NewAttestationsUseCurrentKey
// Test: HmacAttestationService_KeyFromUserSecrets_NotFromAppsettings (verify Key Vault/UserSecrets sourcing)
```

---

## 10. Plan Step Executors

### Infrastructure.AI.Tests/Planner/StepExecutors/

```csharp
// LlmCallStepExecutor
// Test: Execute_ValidConfig_DelegatesToRunConversationCommand
// Test: Execute_StreamsTokens_NotifiesProgressNotifier
// Test: Execute_LlmFailure_ReturnsFailedResult

// ToolUseStepExecutor
// Test: Execute_ValidTool_RoutesToSandbox
// Test: Execute_AttestationVerificationFails_ReturnsFailedResult
// Test: Execute_SandboxTimeout_ReturnsFail
// Test: Execute_IsolationElevation_SupervisedTierUsesContainer

// HumanGateStepExecutor
// Test: Execute_QueuesEscalation_TransitionsToBlocked (non-blocking)
// Test: Execute_DoesNotCallRequestEscalationAsync (blocking mode forbidden)
// Test: Execute_ApprovedEscalation_TransitionsToCompleted
// Test: Execute_RejectedEscalation_TransitionsToFailed

// ConditionalBranchStepExecutor
// Test: Execute_TrueCondition_ActivatesTrueEdge
// Test: Execute_FalseCondition_ActivatesFalseEdge
// Test: Execute_InvalidExpression_ReturnsFailedResult
// Test: Execute_UsesDecisionRulePattern_NotCustomEvaluator
// Test: Execute_InjectionAttempt_RejectedBySanitization

// SubPlanStepExecutor
// Test: Execute_CreatesNewDiScope_IsolatesContext
// Test: Execute_ChildCompletes_ReturnsChildOutput
// Test: Execute_ChildFails_ReturnsFailedResult
// Test: Execute_ExceedsMaxDepth_ReturnsFail
// Test: Execute_ChildPlan_LinkedViaParentPlanId
```

---

## 11. Plan Executor

### Infrastructure.AI.Tests/Planner/

```csharp
// Test: Execute_LinearPlan_RunsStepsInOrder (A->B->C)
// Test: Execute_ParallelPlan_RunsIndependentStepsInParallel (A->[B,C]->D)
// Test: Execute_DiamondDag_StepDWaitsForBothBAndC
// Test: Execute_BoundedConcurrency_RespectsMaxParallelSteps
// Test: Execute_StepFails_DependentSubgraphSkipped
// Test: Execute_StepFails_IndependentBranchContinues
// Test: Execute_BlockedStep_IndependentBranchesContinue
// Test: Execute_BlockedStep_ResolvesOnNextPass
// Test: Execute_PlanTimeout_CancelsRunningSteps
// Test: Execute_Checkpoint_PersistsAfterEachTransition
// Test: Execute_Resume_RebuildsReadyQueueFromState
// Test: Execute_ConcurrentSamePlan_SerializedViaKeySemaphore
// Test: Execute_ConditionalBranch_FollowsCorrectPath
// Test: Execute_SubPlan_ChildExecutesInIsolatedScope
// Test: Execute_EmitsPlanStarted_OnBegin
// Test: Execute_EmitsPlanCompleted_OnSuccess
// Test: Execute_EmitsPlanFailed_OnFailure
// Test: Execute_EmitsStepStarted_ForEachStep
// Test: Execute_EmitsStateUpdate_OnEachTransition
```

---

## 12. Plan Generator

### Infrastructure.AI.Tests/Planner/

```csharp
// Test: GenerateAsync_ValidTask_ReturnsPlanGraph
// Test: GenerateAsync_LlmOutput_ValidatedBeforeReturn
// Test: GenerateAsync_InvalidLlmOutput_ReturnsFail
// Test: GenerateAsync_OutputPassesAllValidationChecks
```

---

## 13. CQRS Commands/Queries

### Application.Core.Tests/CQRS/Planner/

```csharp
// Test: CreatePlanCommandHandler_ValidPlan_PersistsAndReturnsPlanId
// Test: CreatePlanCommandHandler_InvalidPlan_ReturnsValidationFailure
// Test: GeneratePlanCommandHandler_ValidTask_GeneratesAndPersistsPlan
// Test: ExecutePlanCommandHandler_NewPlan_StartsExecution
// Test: ExecutePlanCommandHandler_ExistingPlan_ResumesFromCheckpoint
// Test: CancelPlanCommandHandler_RunningPlan_MarksStepsAsSkipped
// Test: RetryPlanStepCommandHandler_FailedStep_RestartsStep
// Test: GetPlanQueryHandler_ExistingPlan_ReturnsGraphAndState
// Test: GetPlanHistoryQueryHandler_ExecutedPlan_ReturnsAuditTrail
// Test: ListPlansQueryHandler_WithFilters_ReturnsMa tchingPlans
```

---

## 14. AG-UI Events

### Presentation.AgentHub.Tests/Planner/

```csharp
// Test: AgUiPlanProgressNotifier_PlanStarted_EmitsPlanStartedEvent
// Test: AgUiPlanProgressNotifier_StepStarted_EmitsStepStartedEvent
// Test: AgUiPlanProgressNotifier_StepCompleted_EmitsStepCompletedEvent
// Test: AgUiPlanProgressNotifier_StateUpdate_EmitsStateDeltaEvent
// Test: AgUiPlanProgressNotifier_SandboxStatus_EmitsSandboxStatusEvent
// Test: AgUiPlanProgressNotifier_PlanCompleted_EmitsPlanCompletedEvent
// Test: AgUiPlanProgressNotifier_PlanFailed_EmitsPlanFailedEvent
// Test: PlanStartedEvent_JsonSerialization_IncludesTypeDiscriminator
// Test: PlanStateUpdateEvent_JsonPatch_ValidRfc6902Format
```

---

## 15. DI Registration

### Integration test verifying full DI container resolves:

```csharp
// Test: DependencyInjection_AllPlannerServices_Resolvable
// Test: DependencyInjection_AllSandboxServices_Resolvable
// Test: DependencyInjection_KeyedStepExecutors_ResolveAllFiveTypes
// Test: DependencyInjection_KeyedSandboxExecutors_ResolveBothTiers
// Test: DependencyInjection_PlannerDbContext_ScopedLifetime
// Test: DependencyInjection_DbContextFactory_AvailableForSingletons
```

---

## 16. Configuration

### Infrastructure.AI.Tests/Configuration/

```csharp
// Test: PlannerOptions_Binding_ReadsFromAppSettings
// Test: PlannerOptions_Defaults_MaxConcurrentPlans50_MaxParallelSteps10
// Test: SandboxOptions_Binding_ReadsFromAppSettings
// Test: SandboxOptions_ToolOverrides_ParsedCorrectly
// Test: SandboxOptions_Defaults_ProcessIsolation_256MbMemory
```

---

## Testing Strategy Notes

- **Unit tests**: Domain models, validators, capability enforcement, attestation, step executors (mock sandbox/escalation/chat client)
- **Integration tests**: Plan executor with in-memory SQLite, EF Core persistence round-trips, DI container resolution
- **Platform-specific**: Windows Job Object tests marked `[Trait("Category", "WindowsOnly")]`, skipped on Linux CI
- **Docker-dependent**: Docker sandbox tests marked `[Trait("Category", "Integration")]`, skipped when Docker daemon unavailable
- **In-memory SQLite caveat**: No WAL mode support. Concurrency tests use file-based SQLite with actual WAL mode for realistic behavior.
- **Mock boundaries**: Mock `ISandboxExecutor` for plan executor tests, mock `IChatClient` for LLM step tests, mock `IEscalationService` for human gate tests. Never mock what you're testing.
