# Unified Model Router Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `IRagModelRouter` with a unified `IModelRouter` that routes agent turns, RAG operations, and supervisor delegation based on task complexity classification with hybrid heuristic + LLM classification and auto-escalation.

**Architecture:** New domain models in `Domain.AI/Routing/`, new interfaces in `Application.AI.Common/Interfaces/Routing/`, implementations in `Infrastructure.AI/Routing/`. Existing RAG consumers migrate from `IRagModelRouter` to `IModelRouter`. Config consolidates `ModelTieringConfig` + `ComplexityRoutingConfig` into `ModelRoutingConfig`. AgentFactory and Supervisor gain complexity-aware routing.

**Tech Stack:** C# .NET 10, Microsoft.Extensions.AI (IChatClient), MediatR, FluentValidation, IOptionsMonitor, IMemoryCache, xUnit, Moq

**Spec:** `docs/superpowers/specs/2026-05-22-unified-model-router-design.md`

---

## File Structure

### New Files (Create)

| File | Project | Responsibility |
|------|---------|---------------|
| `Domain.AI/Routing/Enums/TaskComplexity.cs` | Domain.AI | 4-value enum: Trivial, Simple, Moderate, Complex |
| `Domain.AI/Routing/Enums/ClassificationSource.cs` | Domain.AI | 2-value enum: Heuristic, LlmClassifier |
| `Domain.AI/Routing/Enums/TurnOutcome.cs` | Domain.AI | 5-value enum: Success, UserCorrection, RetryRequested, ToolFailure, Timeout |
| `Domain.AI/Routing/Models/ModelTier.cs` | Domain.AI | Tier definition record (name, client type, deployment, cost) |
| `Domain.AI/Routing/Models/TaskComplexityAssessment.cs` | Domain.AI | Classification result record |
| `Domain.AI/Routing/Models/ModelRoutingDecision.cs` | Domain.AI | Full routing decision record (tier + client + complexity) |
| `Domain.AI/Routing/Models/AgentTurnContext.cs` | Domain.AI | Input context for classification |
| `Domain.Common/Config/AI/Routing/ModelRoutingConfig.cs` | Domain.Common | Config POCO consolidating tiers + heuristics + escalation + retrieval defaults |
| `Application.AI.Common/Interfaces/Routing/IModelRouter.cs` | Application.AI.Common | Unified routing interface (4 methods) |
| `Application.AI.Common/Interfaces/Routing/ITaskComplexityHeuristic.cs` | Application.AI.Common | Fast heuristic classifier interface |
| `Application.AI.Common/Interfaces/Routing/ITaskComplexityClassifier.cs` | Application.AI.Common | LLM-based classifier interface |
| `Application.AI.Common/Interfaces/Routing/IEscalationTracker.cs` | Application.AI.Common | Per-conversation escalation state interface |
| `Infrastructure.AI/Routing/TaskComplexityHeuristic.cs` | Infrastructure.AI | Heuristic classification rules engine |
| `Infrastructure.AI/Routing/TaskComplexityClassifier.cs` | Infrastructure.AI | LLM few-shot classifier (adapted from QueryComplexityClassifier) |
| `Infrastructure.AI/Routing/EscalationTracker.cs` | Infrastructure.AI | In-memory escalation state machine |
| `Infrastructure.AI/Routing/ModelRouter.cs` | Infrastructure.AI | Unified router orchestrating heuristic→LLM→escalation→client |
| `Domain.AI.Tests/Routing/TaskComplexityTests.cs` | Tests | Domain model tests |
| `Infrastructure.AI.Tests/Routing/TaskComplexityHeuristicTests.cs` | Tests | Heuristic classifier table-driven tests |
| `Infrastructure.AI.Tests/Routing/EscalationTrackerTests.cs` | Tests | Escalation state machine tests |
| `Infrastructure.AI.Tests/Routing/TaskComplexityClassifierTests.cs` | Tests | LLM classifier mock tests |
| `Infrastructure.AI.Tests/Routing/ModelRouterTests.cs` | Tests | Integration tests for full routing pipeline |

### Modified Files

| File | Change |
|------|--------|
| `Infrastructure.AI/DependencyInjection.cs` | Register IModelRouter, ITaskComplexityHeuristic, ITaskComplexityClassifier, IEscalationTracker |
| `Infrastructure.AI.RAG/DependencyInjection.cs` | Remove IRagModelRouter + IQueryComplexityClassifier registrations, update consumer resolutions |
| `Infrastructure.AI.KnowledgeGraph/DependencyInjection.cs` | Update IRagModelRouter → IModelRouter resolution |
| `Application.AI.Common/Factories/AgentFactory.cs` | Add optional IModelRouter for turn routing |
| `Domain.AI/Orchestration/SupervisorDecisionContext.cs` | Add TaskComplexityAssessment? field |
| `Infrastructure.AI/Agents/CapabilityMatchSupervisor.cs` | Factor complexity into agent selection |
| `Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs` | Migrate ComplexityClassification → TaskComplexityAssessment |
| `Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs` | Migrate IQueryComplexityClassifier → ITaskComplexityClassifier |
| `Infrastructure.AI.RAG/Ingestion/ExtractEntitiesExecutor.cs` | Migrate IRagModelRouter → IModelRouter |
| `Infrastructure.AI.RAG/Evaluation/CragEvaluator.cs` | Migrate IRagModelRouter → IModelRouter |
| `Infrastructure.AI.RAG/Evaluation/AnswerFaithfulnessEvaluator.cs` | Migrate IRagModelRouter → IModelRouter |
| `Infrastructure.AI.RAG/Evaluation/RetrievalQualityEvaluator.cs` | Migrate IRagModelRouter → IModelRouter |
| `Infrastructure.AI.RAG/Evaluation/SufficiencyEvaluator.cs` | Migrate IRagModelRouter → IModelRouter |
| `Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs` | Migrate QueryComplexity → TaskComplexity |
| `Infrastructure.AI.KnowledgeGraph/Ingestion/KgIngestionWorkflow.cs` | Migrate IRagModelRouter → IModelRouter |
| `Infrastructure.AI.KnowledgeGraph/Feedback/LlmFeedbackDetector.cs` | Migrate IRagModelRouter → IModelRouter |
| `Domain.Common/Config/AI/AIConfig.cs` | Add ModelRouting property |
| 6+ test files | Update mocks from old types to new types |

### Removed Files (after migration)

| File | Replaced By |
|------|-------------|
| `Application.AI.Common/Interfaces/RAG/IRagModelRouter.cs` | `IModelRouter.RouteOperationAsync()` |
| `Application.AI.Common/Interfaces/RAG/IQueryComplexityClassifier.cs` | `ITaskComplexityClassifier.ClassifyAsync()` |
| `Infrastructure.AI.RAG/Ingestion/RagModelRouter.cs` | `Infrastructure.AI/Routing/ModelRouter.cs` |
| `Infrastructure.AI.RAG/QueryTransform/QueryComplexityClassifier.cs` | `Infrastructure.AI/Routing/TaskComplexityClassifier.cs` |
| `Domain.AI/RAG/Enums/QueryComplexity.cs` | `Domain.AI/Routing/Enums/TaskComplexity.cs` |
| `Domain.AI/RAG/Models/ComplexityClassification.cs` | `Domain.AI/Routing/Models/TaskComplexityAssessment.cs` |
| `Domain.Common/Config/AI/RAG/ModelTieringConfig.cs` | `Domain.Common/Config/AI/Routing/ModelRoutingConfig.cs` |
| `Domain.Common/Config/AI/RAG/ComplexityRoutingConfig.cs` | `Domain.Common/Config/AI/Routing/ModelRoutingConfig.cs` |
| `Domain.Common/Config/AI/RAG/ModelTierDefinition.cs` | `Domain.AI/Routing/Models/ModelTier.cs` |

---

## Dependency Graph

```
Task 1 (Domain Models) ← no dependencies
Task 2 (Config POCOs) ← depends on Task 1 (uses TaskComplexity, ModelTier)
Task 3 (Interfaces) ← depends on Task 1, Task 2
Task 4 (Heuristic Impl) ← depends on Task 3
Task 5 (Classifier Impl) ← depends on Task 3
Task 6 (Escalation Impl) ← depends on Task 3
Task 7 (ModelRouter Impl) ← depends on Tasks 4, 5, 6
Task 8 (DI Registration) ← depends on Task 7
Task 9 (RAG Migration) ← depends on Task 8
Task 10 (AgentFactory Integration) ← depends on Task 8
Task 11 (Supervisor Integration) ← depends on Task 8
Task 12 (Remove Old Types) ← depends on Tasks 9, 10, 11
Task 13 (Test Migration) ← depends on Task 12
Task 14 (Build Verify) ← depends on Task 13
```

Tasks 4, 5, 6 are independent (parallel). Tasks 9, 10, 11 are independent (parallel).

---

### Task 1: Domain Models (Enums + Records)

**Files:**
- Create: `src/Content/Domain/Domain.AI/Routing/Enums/TaskComplexity.cs`
- Create: `src/Content/Domain/Domain.AI/Routing/Enums/ClassificationSource.cs`
- Create: `src/Content/Domain/Domain.AI/Routing/Enums/TurnOutcome.cs`
- Create: `src/Content/Domain/Domain.AI/Routing/Models/ModelTier.cs`
- Create: `src/Content/Domain/Domain.AI/Routing/Models/TaskComplexityAssessment.cs`
- Create: `src/Content/Domain/Domain.AI/Routing/Models/ModelRoutingDecision.cs`
- Create: `src/Content/Domain/Domain.AI/Routing/Models/AgentTurnContext.cs`
- Test: `src/Content/Tests/Domain.AI.Tests/Routing/TaskComplexityAssessmentTests.cs`

- [ ] **Step 1: Create TaskComplexity enum**

```csharp
// src/Content/Domain/Domain.AI/Routing/Enums/TaskComplexity.cs
namespace Domain.AI.Routing.Enums;

/// <summary>
/// Classifies the complexity of a task for model tier selection.
/// Ordered from least to most complex — tier escalation moves upward.
/// </summary>
public enum TaskComplexity
{
    /// <summary>Parametric knowledge, simple lookup, greeting, acknowledgment.</summary>
    Trivial = 0,

    /// <summary>Single-step reasoning, basic tool use, straightforward Q&amp;A.</summary>
    Simple = 1,

    /// <summary>Multi-step reasoning, multiple tools, synthesis, comparison.</summary>
    Moderate = 2,

    /// <summary>Deep reasoning, multi-hop, code generation, planning, architectural decisions.</summary>
    Complex = 3
}
```

- [ ] **Step 2: Create ClassificationSource enum**

```csharp
// src/Content/Domain/Domain.AI/Routing/Enums/ClassificationSource.cs
namespace Domain.AI.Routing.Enums;

/// <summary>Identifies how a complexity classification was determined.</summary>
public enum ClassificationSource
{
    /// <summary>Fast heuristic rules (zero LLM cost).</summary>
    Heuristic,

    /// <summary>LLM-based few-shot classification (fallback for ambiguous cases).</summary>
    LlmClassifier
}
```

- [ ] **Step 3: Create TurnOutcome enum**

```csharp
// src/Content/Domain/Domain.AI/Routing/Enums/TurnOutcome.cs
namespace Domain.AI.Routing.Enums;

/// <summary>
/// Outcome signal for a completed agent turn.
/// Fed to <see cref="Domain.AI.Routing.Models.ModelTier"/> escalation tracking.
/// </summary>
public enum TurnOutcome
{
    /// <summary>Turn completed successfully, user moved on.</summary>
    Success,

    /// <summary>User corrected the response ("no", "that's wrong").</summary>
    UserCorrection,

    /// <summary>User asked to try again or rephrase.</summary>
    RetryRequested,

    /// <summary>A tool call failed during the turn.</summary>
    ToolFailure,

    /// <summary>Model response timed out.</summary>
    Timeout
}
```

- [ ] **Step 4: Create ModelTier record**

```csharp
// src/Content/Domain/Domain.AI/Routing/Models/ModelTier.cs
using Domain.Common.Config.AI;

namespace Domain.AI.Routing.Models;

/// <summary>
/// Defines a model deployment tier with provider, cost, and rate limit metadata.
/// Tiers are ordered by <see cref="EstimatedCostPer1KTokens"/> ascending for escalation.
/// </summary>
public sealed record ModelTier
{
    /// <summary>Tier identifier (e.g., "economy", "standard", "premium").</summary>
    public required string Name { get; init; }

    /// <summary>Which AI provider hosts this tier's deployment.</summary>
    public required AIAgentFrameworkClientType ClientType { get; init; }

    /// <summary>Deployment name or model identifier for the provider.</summary>
    public required string DeploymentName { get; init; }

    /// <summary>Optional reference to a named fallback chain in ResilienceConfig.</summary>
    public string? FallbackChainName { get; init; }

    /// <summary>Rate limit for this tier (tokens per minute).</summary>
    public int MaxTokensPerMinute { get; init; }

    /// <summary>Estimated cost per 1K tokens for budget tracking and tier ordering.</summary>
    public decimal EstimatedCostPer1KTokens { get; init; }
}
```

- [ ] **Step 5: Create AgentTurnContext record**

```csharp
// src/Content/Domain/Domain.AI/Routing/Models/AgentTurnContext.cs
namespace Domain.AI.Routing.Models;

/// <summary>
/// Input context for classifying the complexity of an agent conversation turn.
/// Provides the signals that heuristic and LLM classifiers use.
/// </summary>
public sealed record AgentTurnContext
{
    /// <summary>Conversation identifier for escalation state tracking.</summary>
    public required string ConversationId { get; init; }

    /// <summary>The user's message text for this turn.</summary>
    public required string UserMessage { get; init; }

    /// <summary>Sequential turn number in the conversation (1-based).</summary>
    public required int TurnNumber { get; init; }

    /// <summary>Number of tools available to the agent for this turn.</summary>
    public int AvailableToolCount { get; init; }

    /// <summary>Total conversation depth (may differ from TurnNumber in multi-agent scenarios).</summary>
    public int ConversationDepth { get; init; }

    /// <summary>Names of tools used in recent turns (for tool-chain detection).</summary>
    public IReadOnlyList<string>? RecentToolNames { get; init; }
}
```

- [ ] **Step 6: Create TaskComplexityAssessment record**

```csharp
// src/Content/Domain/Domain.AI/Routing/Models/TaskComplexityAssessment.cs
using Domain.AI.Routing.Enums;

namespace Domain.AI.Routing.Models;

/// <summary>
/// Result of classifying a task's complexity. Used by the router
/// to select a model tier and by the supervisor for delegation.
/// </summary>
public sealed record TaskComplexityAssessment
{
    /// <summary>Classified complexity level.</summary>
    public required TaskComplexity Complexity { get; init; }

    /// <summary>Classification confidence (0.0–1.0).</summary>
    public required double Confidence { get; init; }

    /// <summary>How this classification was determined.</summary>
    public required ClassificationSource Source { get; init; }

    /// <summary>Optional explanation of why this complexity was chosen.</summary>
    public string? Reasoning { get; init; }

    /// <summary>Whether retrieval should be skipped (true for Trivial tasks).</summary>
    public bool SkipRetrieval => Complexity == TaskComplexity.Trivial;
}
```

- [ ] **Step 7: Create ModelRoutingDecision record**

```csharp
// src/Content/Domain/Domain.AI/Routing/Models/ModelRoutingDecision.cs
using Domain.AI.Routing.Enums;
using Microsoft.Extensions.AI;

namespace Domain.AI.Routing.Models;

/// <summary>
/// Complete routing decision: which model tier was selected, the resolved client,
/// the complexity assessment, and whether escalation was applied.
/// </summary>
public sealed record ModelRoutingDecision
{
    /// <summary>The selected model tier.</summary>
    public required ModelTier SelectedTier { get; init; }

    /// <summary>Resolved IChatClient for the selected tier.</summary>
    public required IChatClient Client { get; init; }

    /// <summary>Assessed task complexity.</summary>
    public required TaskComplexity Complexity { get; init; }

    /// <summary>How the complexity was classified.</summary>
    public required ClassificationSource Source { get; init; }

    /// <summary>Classification confidence (0.0–1.0).</summary>
    public required double Confidence { get; init; }

    /// <summary>Optional reasoning from the classifier.</summary>
    public string? Reasoning { get; init; }

    /// <summary>True if this decision was escalated from a lower tier due to quality signals.</summary>
    public bool WasEscalated { get; init; }
}
```

- [ ] **Step 8: Write domain model tests**

```csharp
// src/Content/Tests/Domain.AI.Tests/Routing/TaskComplexityAssessmentTests.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;

namespace Domain.AI.Tests.Routing;

public class TaskComplexityAssessmentTests
{
    [Fact]
    public void SkipRetrieval_Trivial_ReturnsTrue()
    {
        var assessment = new TaskComplexityAssessment
        {
            Complexity = TaskComplexity.Trivial,
            Confidence = 0.95,
            Source = ClassificationSource.Heuristic
        };

        Assert.True(assessment.SkipRetrieval);
    }

    [Theory]
    [InlineData(TaskComplexity.Simple)]
    [InlineData(TaskComplexity.Moderate)]
    [InlineData(TaskComplexity.Complex)]
    public void SkipRetrieval_NonTrivial_ReturnsFalse(TaskComplexity complexity)
    {
        var assessment = new TaskComplexityAssessment
        {
            Complexity = complexity,
            Confidence = 0.9,
            Source = ClassificationSource.Heuristic
        };

        Assert.False(assessment.SkipRetrieval);
    }

    [Fact]
    public void TaskComplexity_ValuesAreOrdered()
    {
        Assert.True(TaskComplexity.Trivial < TaskComplexity.Simple);
        Assert.True(TaskComplexity.Simple < TaskComplexity.Moderate);
        Assert.True(TaskComplexity.Moderate < TaskComplexity.Complex);
    }

    [Fact]
    public void ModelTier_RecordEquality()
    {
        var tier1 = new ModelTier
        {
            Name = "economy",
            ClientType = Domain.Common.Config.AI.AIAgentFrameworkClientType.OpenAI,
            DeploymentName = "gpt-4o-mini",
            EstimatedCostPer1KTokens = 0.00015m
        };

        var tier2 = tier1 with { Name = "standard" };

        Assert.NotEqual(tier1, tier2);
        Assert.Equal("economy", tier1.Name);
        Assert.Equal("standard", tier2.Name);
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Domain.AI.Tests/Domain.AI.Tests.csproj --filter "FullyQualifiedName~Routing" -v n`
Expected: 5 tests pass

- [ ] **Step 10: Commit**

```bash
git add src/Content/Domain/Domain.AI/Routing/ src/Content/Tests/Domain.AI.Tests/Routing/
git commit -m "feat(routing): add domain models for unified model router

TaskComplexity, ClassificationSource, TurnOutcome enums.
ModelTier, AgentTurnContext, TaskComplexityAssessment, ModelRoutingDecision records."
```

---

### Task 2: Configuration POCOs

**Files:**
- Create: `src/Content/Domain/Domain.Common/Config/AI/Routing/ModelRoutingConfig.cs`
- Modify: `src/Content/Domain/Domain.Common/Config/AI/AIConfig.cs` (add ModelRouting property)

- [ ] **Step 1: Create ModelRoutingConfig**

This consolidates `ModelTieringConfig`, `ComplexityRoutingConfig`, escalation settings, and heuristic thresholds into one config POCO.

```csharp
// src/Content/Domain/Domain.Common/Config/AI/Routing/ModelRoutingConfig.cs
namespace Domain.Common.Config.AI.Routing;

/// <summary>
/// Configuration for the unified model router.
/// Consolidates model tiering, complexity routing, escalation, and heuristic thresholds.
/// Bound to <c>AppConfig:AI:ModelRouting</c>.
/// </summary>
public sealed class ModelRoutingConfig
{
    /// <summary>Master toggle for complexity-aware routing. When false, all routing falls back to DefaultTier.</summary>
    public bool Enabled { get; set; }

    /// <summary>Tier name to use when routing is disabled or classification fails.</summary>
    public string DefaultTier { get; set; } = "standard";

    /// <summary>Minimum heuristic confidence to accept without LLM fallback (0.0–1.0).</summary>
    public double HeuristicConfidenceThreshold { get; set; } = 0.8;

    /// <summary>Available model tiers ordered by cost ascending.</summary>
    public ModelRoutingTierConfig[] Tiers { get; set; } = [];

    /// <summary>Per-operation tier overrides for RAG pipeline steps.</summary>
    public Dictionary<string, string> OperationOverrides { get; set; } = new();

    /// <summary>Auto-escalation settings.</summary>
    public EscalationConfig Escalation { get; set; } = new();

    /// <summary>Heuristic classification signal thresholds.</summary>
    public HeuristicThresholdsConfig HeuristicThresholds { get; set; } = new();

    /// <summary>Default retrieval parameters per complexity tier (for RetrievalDecisionGate).</summary>
    public RetrievalDefaultsConfig RetrievalDefaults { get; set; } = new();
}

/// <summary>Defines a single model tier: provider, deployment, cost, and rate limit.</summary>
public sealed class ModelRoutingTierConfig
{
    /// <summary>Tier identifier (e.g., "economy", "standard", "premium").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>AI provider for this tier.</summary>
    public AIAgentFrameworkClientType ClientType { get; set; } = AIAgentFrameworkClientType.AzureOpenAI;

    /// <summary>Deployment name or model identifier.</summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>Optional reference to a named fallback chain in ResilienceConfig.</summary>
    public string? FallbackChainName { get; set; }

    /// <summary>Tokens-per-minute rate limit.</summary>
    public int MaxTokensPerMinute { get; set; }

    /// <summary>Estimated cost per 1K tokens (used for tier ordering and budget tracking).</summary>
    public decimal EstimatedCostPer1KTokens { get; set; }
}

/// <summary>Auto-escalation configuration.</summary>
public sealed class EscalationConfig
{
    /// <summary>Enable auto-escalation on quality signals.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Block escalation when session spend exceeds this % of budget.</summary>
    public int BudgetCeilingPercent { get; set; } = 80;

    /// <summary>Turns to stay at escalated tier before attempting downshift.</summary>
    public int CooldownTurns { get; set; } = 2;
}

/// <summary>Thresholds for the heuristic complexity classifier.</summary>
public sealed class HeuristicThresholdsConfig
{
    /// <summary>Messages shorter than this are Trivial candidates.</summary>
    public int TrivialMaxLength { get; set; } = 50;

    /// <summary>Messages shorter than this are Simple candidates.</summary>
    public int SimpleMaxLength { get; set; } = 200;

    /// <summary>Messages shorter than this are Moderate candidates.</summary>
    public int ModerateMaxLength { get; set; } = 1000;

    /// <summary>Available tool count above which the task is Complex.</summary>
    public int ComplexMinToolCount { get; set; } = 8;

    /// <summary>Keywords that signal Complex tasks.</summary>
    public string[] ComplexKeywords { get; set; } = ["refactor", "design", "plan", "architect", "migrate", "rewrite"];

    /// <summary>Keywords that signal Trivial tasks (greetings, acknowledgments).</summary>
    public string[] TrivialKeywords { get; set; } = ["hi", "hello", "thanks", "ok", "yes", "no"];
}

/// <summary>Default retrieval parameters per complexity tier (used by RetrievalDecisionGate).</summary>
public sealed class RetrievalDefaultsConfig
{
    /// <summary>Minimum confidence to accept a complexity classification for retrieval decisions.</summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>TopK for Simple queries.</summary>
    public int SimpleTopK { get; set; } = 5;

    /// <summary>TopK for Complex queries.</summary>
    public int ComplexTopK { get; set; } = 15;

    /// <summary>Skip reranking for Simple queries.</summary>
    public bool SkipRerankForSimple { get; set; } = true;

    /// <summary>Skip CRAG evaluation for Simple queries.</summary>
    public bool SkipCragForSimple { get; set; } = true;
}
```

- [ ] **Step 2: Add ModelRouting to AIConfig**

Read `src/Content/Domain/Domain.Common/Config/AI/AIConfig.cs` and add a `ModelRouting` property alongside the existing `Rag`, `Resilience`, etc. properties.

Add this property to the `AIConfig` class:

```csharp
/// <summary>Unified model routing configuration (complexity-aware tier selection).</summary>
public ModelRoutingConfig? ModelRouting { get; set; }
```

Add the using directive at the top of the file:

```csharp
using Domain.Common.Config.AI.Routing;
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/Content/Domain/Domain.Common/Domain.Common.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/Content/Domain/Domain.Common/Config/AI/Routing/ src/Content/Domain/Domain.Common/Config/AI/AIConfig.cs
git commit -m "feat(routing): add ModelRoutingConfig consolidating tier, escalation, and heuristic settings"
```

---

### Task 3: Application Interfaces

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Routing/IModelRouter.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Routing/ITaskComplexityHeuristic.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Routing/ITaskComplexityClassifier.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Routing/IEscalationTracker.cs`

- [ ] **Step 1: Create IModelRouter**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/Routing/IModelRouter.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// Unified model router that handles all model selection decisions:
/// agent turn routing, RAG operation routing, and supervisor delegation advisory.
/// Replaces <c>IRagModelRouter</c>.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Routes an agent conversation turn to the appropriate model tier.
    /// Classifies complexity via heuristic (fast) then LLM (fallback), applies escalation.
    /// </summary>
    Task<ModelRoutingDecision> RouteAgentTurnAsync(
        AgentTurnContext turnContext,
        CancellationToken ct = default);

    /// <summary>
    /// Routes a named operation (e.g., RAG pipeline step) to its configured model tier.
    /// Uses <c>ModelRoutingConfig.OperationOverrides</c> for tier mapping.
    /// </summary>
    Task<ModelRoutingDecision> RouteOperationAsync(
        string operationName,
        CancellationToken ct = default);

    /// <summary>
    /// Assesses task complexity for supervisor delegation decisions.
    /// Advisory only — does not return an IChatClient.
    /// </summary>
    Task<TaskComplexityAssessment> AssessTaskComplexityAsync(
        string taskDescription,
        IReadOnlyList<string> requiredCapabilities,
        CancellationToken ct = default);

    /// <summary>
    /// Reports turn outcome for auto-escalation tracking.
    /// Call after each agent turn completes.
    /// </summary>
    void ReportTurnOutcome(string conversationId, TurnOutcome outcome);
}
```

- [ ] **Step 2: Create ITaskComplexityHeuristic**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/Routing/ITaskComplexityHeuristic.cs
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// Fast, zero-cost heuristic classifier for task complexity.
/// Returns null when confidence is below threshold, triggering LLM fallback.
/// </summary>
public interface ITaskComplexityHeuristic
{
    /// <summary>
    /// Classifies the task complexity based on message signals (length, keywords, tool count, etc.).
    /// Returns null if the heuristic is not confident enough.
    /// </summary>
    TaskComplexityAssessment? Classify(AgentTurnContext context);
}
```

- [ ] **Step 3: Create ITaskComplexityClassifier**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/Routing/ITaskComplexityClassifier.cs
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// LLM-based few-shot complexity classifier. Used as fallback when the heuristic
/// is not confident. Uses the economy-tier model for classification.
/// Replaces <c>IQueryComplexityClassifier</c>.
/// </summary>
public interface ITaskComplexityClassifier
{
    /// <summary>
    /// Classifies task complexity using an LLM with few-shot examples.
    /// Adds ~200ms latency. Falls back to Moderate on any failure.
    /// </summary>
    Task<TaskComplexityAssessment> ClassifyAsync(
        AgentTurnContext context,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create IEscalationTracker**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/Routing/IEscalationTracker.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;

namespace Application.AI.Common.Interfaces.Routing;

/// <summary>
/// Tracks per-conversation quality signals and adjusts model tier accordingly.
/// In-memory state with the same lifetime as the agent conversation cache.
/// </summary>
public interface IEscalationTracker
{
    /// <summary>
    /// Returns the effective tier for a conversation, factoring in recent quality signals.
    /// May return a higher tier than <paramref name="baseComplexity"/> warrants if escalation is active.
    /// </summary>
    ModelTier GetEffectiveTier(
        string conversationId,
        TaskComplexity baseComplexity,
        IReadOnlyList<ModelTier> availableTiers);

    /// <summary>Records a turn outcome for escalation tracking.</summary>
    void RecordOutcome(string conversationId, TurnOutcome outcome);

    /// <summary>Resets escalation state for a conversation (e.g., on conversation end).</summary>
    void Reset(string conversationId);
}
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/Content/Application/Application.AI.Common/Application.AI.Common.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/Routing/
git commit -m "feat(routing): add IModelRouter, ITaskComplexityHeuristic, ITaskComplexityClassifier, IEscalationTracker interfaces"
```

---

### Task 4: Heuristic Classifier Implementation + Tests

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Routing/TaskComplexityHeuristic.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Routing/TaskComplexityHeuristicTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.Tests/Routing/TaskComplexityHeuristicTests.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI.Routing;
using Infrastructure.AI.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Tests.Routing;

public class TaskComplexityHeuristicTests
{
    private readonly TaskComplexityHeuristic _sut;

    public TaskComplexityHeuristicTests()
    {
        var config = new ModelRoutingConfig
        {
            HeuristicConfidenceThreshold = 0.8,
            HeuristicThresholds = new HeuristicThresholdsConfig
            {
                TrivialMaxLength = 50,
                SimpleMaxLength = 200,
                ModerateMaxLength = 1000,
                ComplexMinToolCount = 8,
                ComplexKeywords = ["refactor", "design", "plan", "architect"],
                TrivialKeywords = ["hi", "hello", "thanks", "ok", "yes", "no"]
            }
        };

        var options = Options.Create(config);
        _sut = new TaskComplexityHeuristic(options, NullLogger<TaskComplexityHeuristic>.Instance);
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("thanks")]
    [InlineData("ok")]
    public void Classify_ShortGreeting_ReturnsTrivial(string message)
    {
        var context = MakeContext(message, turnNumber: 1, toolCount: 0);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Trivial, result.Complexity);
        Assert.True(result.Confidence >= 0.8);
        Assert.Equal(ClassificationSource.Heuristic, result.Source);
    }

    [Fact]
    public void Classify_ShortQuestionFewTools_ReturnsSimple()
    {
        var context = MakeContext("What is dependency injection?", turnNumber: 1, toolCount: 2);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Simple, result.Complexity);
        Assert.True(result.Confidence >= 0.8);
    }

    [Fact]
    public void Classify_MediumMessageWithCodeBlock_ReturnsModerate()
    {
        var message = "Can you analyze this code?\n```csharp\npublic class Foo { }\n```";
        var context = MakeContext(message, turnNumber: 4, toolCount: 5);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.True(result.Complexity >= TaskComplexity.Moderate);
    }

    [Fact]
    public void Classify_LongMessageWithComplexKeywords_ReturnsComplex()
    {
        var message = "I need to refactor the entire authentication system. " + new string('x', 1000);
        var context = MakeContext(message, turnNumber: 10, toolCount: 12,
            recentTools: ["file_system", "code_search", "git", "test_runner", "linter"]);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Complex, result.Complexity);
        Assert.True(result.Confidence >= 0.8);
    }

    [Fact]
    public void Classify_AmbiguousMessage_ReturnsNull()
    {
        // Medium length, no keywords, moderate tools — ambiguous
        var context = MakeContext("Show me how the system processes a request", turnNumber: 3, toolCount: 4);

        var result = _sut.Classify(context);

        // May return null (triggers LLM fallback) or low-confidence result
        if (result is not null)
        {
            Assert.Equal(ClassificationSource.Heuristic, result.Source);
        }
    }

    [Fact]
    public void Classify_HighToolCountOnly_ReturnsComplex()
    {
        var context = MakeContext("Do this task", turnNumber: 1, toolCount: 15);

        var result = _sut.Classify(context);

        Assert.NotNull(result);
        Assert.Equal(TaskComplexity.Complex, result.Complexity);
    }

    private static AgentTurnContext MakeContext(
        string message,
        int turnNumber = 1,
        int toolCount = 0,
        IReadOnlyList<string>? recentTools = null) => new()
    {
        ConversationId = "test-conv-001",
        UserMessage = message,
        TurnNumber = turnNumber,
        AvailableToolCount = toolCount,
        ConversationDepth = turnNumber,
        RecentToolNames = recentTools
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~TaskComplexityHeuristicTests" -v n`
Expected: FAIL — `TaskComplexityHeuristic` does not exist yet

- [ ] **Step 3: Implement TaskComplexityHeuristic**

```csharp
// src/Content/Infrastructure/Infrastructure.AI/Routing/TaskComplexityHeuristic.cs
using System.Text.RegularExpressions;
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Routing;

/// <summary>
/// Fast, zero-cost heuristic classifier for task complexity.
/// Evaluates message length, keywords, tool count, conversation depth, and code block presence.
/// Returns null when no tier exceeds the confidence threshold (triggering LLM fallback).
/// </summary>
public sealed class TaskComplexityHeuristic : ITaskComplexityHeuristic
{
    private static readonly Regex CodeBlockPattern = new(@"```", RegexOptions.Compiled);

    private readonly ModelRoutingConfig _config;
    private readonly ILogger<TaskComplexityHeuristic> _logger;

    public TaskComplexityHeuristic(
        IOptions<ModelRoutingConfig> config,
        ILogger<TaskComplexityHeuristic> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public TaskComplexityAssessment? Classify(AgentTurnContext context)
    {
        var scores = new Dictionary<TaskComplexity, double>
        {
            [TaskComplexity.Trivial] = 0.0,
            [TaskComplexity.Simple] = 0.0,
            [TaskComplexity.Moderate] = 0.0,
            [TaskComplexity.Complex] = 0.0
        };

        var thresholds = _config.HeuristicThresholds;
        var message = context.UserMessage;
        var messageLength = message.Length;

        // Signal: Message length
        if (messageLength <= thresholds.TrivialMaxLength)
            scores[TaskComplexity.Trivial] += 0.3;
        else if (messageLength <= thresholds.SimpleMaxLength)
            scores[TaskComplexity.Simple] += 0.3;
        else if (messageLength <= thresholds.ModerateMaxLength)
            scores[TaskComplexity.Moderate] += 0.25;
        else
            scores[TaskComplexity.Complex] += 0.3;

        // Signal: Trivial keywords (greetings)
        var lowerMessage = message.ToLowerInvariant();
        if (thresholds.TrivialKeywords.Any(kw => lowerMessage == kw || lowerMessage.StartsWith(kw + " ") || lowerMessage.StartsWith(kw + "!")))
            scores[TaskComplexity.Trivial] += 0.4;

        // Signal: Complex keywords
        if (thresholds.ComplexKeywords.Any(kw => lowerMessage.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            scores[TaskComplexity.Complex] += 0.3;

        // Signal: Tool count
        if (context.AvailableToolCount == 0)
            scores[TaskComplexity.Trivial] += 0.15;
        else if (context.AvailableToolCount <= 3)
            scores[TaskComplexity.Simple] += 0.2;
        else if (context.AvailableToolCount <= thresholds.ComplexMinToolCount)
            scores[TaskComplexity.Moderate] += 0.2;
        else
            scores[TaskComplexity.Complex] += 0.3;

        // Signal: Code blocks
        if (CodeBlockPattern.IsMatch(message))
            scores[TaskComplexity.Moderate] += 0.25;

        // Signal: Conversation depth + tool chains
        if (context.TurnNumber == 1 && scores[TaskComplexity.Trivial] > 0.3)
            scores[TaskComplexity.Trivial] += 0.15;
        else if (context.TurnNumber >= 8 && (context.RecentToolNames?.Count ?? 0) > 4)
            scores[TaskComplexity.Complex] += 0.2;
        else if (context.TurnNumber >= 4)
            scores[TaskComplexity.Moderate] += 0.15;

        // Select the tier with the highest score
        var best = scores.MaxBy(kv => kv.Value);
        var confidence = Math.Min(best.Value, 1.0);

        if (confidence < _config.HeuristicConfidenceThreshold)
        {
            _logger.LogDebug(
                "Heuristic confidence {Confidence:F2} below threshold {Threshold:F2} for conversation {ConversationId}, deferring to LLM",
                confidence, _config.HeuristicConfidenceThreshold, context.ConversationId);
            return null;
        }

        _logger.LogDebug(
            "Heuristic classified turn as {Complexity} with confidence {Confidence:F2} for conversation {ConversationId}",
            best.Key, confidence, context.ConversationId);

        return new TaskComplexityAssessment
        {
            Complexity = best.Key,
            Confidence = confidence,
            Source = ClassificationSource.Heuristic,
            Reasoning = $"Heuristic signals: length={messageLength}, tools={context.AvailableToolCount}, turn={context.TurnNumber}"
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~TaskComplexityHeuristicTests" -v n`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Routing/TaskComplexityHeuristic.cs src/Content/Tests/Infrastructure.AI.Tests/Routing/TaskComplexityHeuristicTests.cs
git commit -m "feat(routing): implement TaskComplexityHeuristic with signal-based scoring"
```

---

### Task 5: LLM Classifier Implementation + Tests

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Routing/TaskComplexityClassifier.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Routing/TaskComplexityClassifierTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.Tests/Routing/TaskComplexityClassifierTests.cs
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Infrastructure.AI.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Infrastructure.AI.Tests.Routing;

public class TaskComplexityClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_ValidResponse_ReturnsAssessment()
    {
        var mockRouter = new Mock<IModelRouter>();
        var mockClient = new Mock<IChatClient>();
        var routingDecision = MakeDecision(mockClient.Object);
        mockRouter
            .Setup(r => r.RouteOperationAsync("complexity_classification", It.IsAny<CancellationToken>()))
            .ReturnsAsync(routingDecision);

        var responseJson = """{"complexity": "moderate", "confidence": 0.85, "reasoning": "Multi-step analysis required"}""";
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseJson)));

        var classifier = new TaskComplexityClassifier(mockRouter.Object, NullLogger<TaskComplexityClassifier>.Instance);

        var context = new AgentTurnContext
        {
            ConversationId = "test-001",
            UserMessage = "Analyze the dependency graph and suggest improvements",
            TurnNumber = 3,
            AvailableToolCount = 5
        };

        var result = await classifier.ClassifyAsync(context);

        Assert.Equal(TaskComplexity.Moderate, result.Complexity);
        Assert.Equal(0.85, result.Confidence);
        Assert.Equal(ClassificationSource.LlmClassifier, result.Source);
        Assert.NotNull(result.Reasoning);
    }

    [Fact]
    public async Task ClassifyAsync_InvalidResponse_FallsBackToModerate()
    {
        var mockRouter = new Mock<IModelRouter>();
        var mockClient = new Mock<IChatClient>();
        var routingDecision = MakeDecision(mockClient.Object);
        mockRouter
            .Setup(r => r.RouteOperationAsync("complexity_classification", It.IsAny<CancellationToken>()))
            .ReturnsAsync(routingDecision);

        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "not valid json")));

        var classifier = new TaskComplexityClassifier(mockRouter.Object, NullLogger<TaskComplexityClassifier>.Instance);

        var context = new AgentTurnContext
        {
            ConversationId = "test-002",
            UserMessage = "Do something",
            TurnNumber = 1
        };

        var result = await classifier.ClassifyAsync(context);

        Assert.Equal(TaskComplexity.Moderate, result.Complexity);
        Assert.Equal(0.5, result.Confidence);
        Assert.Equal(ClassificationSource.LlmClassifier, result.Source);
    }

    [Fact]
    public async Task ClassifyAsync_ClientThrows_FallsBackToModerate()
    {
        var mockRouter = new Mock<IModelRouter>();
        var mockClient = new Mock<IChatClient>();
        var routingDecision = MakeDecision(mockClient.Object);
        mockRouter
            .Setup(r => r.RouteOperationAsync("complexity_classification", It.IsAny<CancellationToken>()))
            .ReturnsAsync(routingDecision);

        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var classifier = new TaskComplexityClassifier(mockRouter.Object, NullLogger<TaskComplexityClassifier>.Instance);

        var context = new AgentTurnContext
        {
            ConversationId = "test-003",
            UserMessage = "Do something",
            TurnNumber = 1
        };

        var result = await classifier.ClassifyAsync(context);

        Assert.Equal(TaskComplexity.Moderate, result.Complexity);
        Assert.Equal(0.5, result.Confidence);
    }

    private static ModelRoutingDecision MakeDecision(IChatClient client) => new()
    {
        SelectedTier = new ModelTier
        {
            Name = "economy",
            ClientType = Domain.Common.Config.AI.AIAgentFrameworkClientType.OpenAI,
            DeploymentName = "gpt-4o-mini",
            EstimatedCostPer1KTokens = 0.00015m
        },
        Client = client,
        Complexity = TaskComplexity.Simple,
        Source = ClassificationSource.Heuristic,
        Confidence = 0.9
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~TaskComplexityClassifierTests" -v n`
Expected: FAIL — `TaskComplexityClassifier` does not exist yet

- [ ] **Step 3: Implement TaskComplexityClassifier**

Adapted from the existing `QueryComplexityClassifier` — same few-shot prompt pattern, but broader `TaskComplexity` enum and `AgentTurnContext` input.

```csharp
// src/Content/Infrastructure/Infrastructure.AI/Routing/TaskComplexityClassifier.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Routing;

/// <summary>
/// LLM-based few-shot complexity classifier. Used as fallback when the heuristic
/// is not confident. Routes through IModelRouter to use the economy-tier model.
/// </summary>
public sealed class TaskComplexityClassifier : ITaskComplexityClassifier
{
    private static readonly TaskComplexityAssessment FallbackAssessment = new()
    {
        Complexity = TaskComplexity.Moderate,
        Confidence = 0.5,
        Source = ClassificationSource.LlmClassifier,
        Reasoning = "Fallback — classification failed or was ambiguous"
    };

    private const string SystemPrompt = """
        You are a task complexity classifier. Given a user message and context, classify its complexity.

        ## Complexity Levels

        - **trivial**: Greetings, acknowledgments, simple yes/no answers, parametric knowledge lookups.
          Examples: "hi", "thanks", "what does DI stand for?"

        - **simple**: Single-step reasoning, one tool usage, straightforward Q&A.
          Examples: "show me the file structure", "what's in this config?", "list all endpoints"

        - **moderate**: Multi-step reasoning, multiple tools, synthesis across files, comparison.
          Examples: "compare these two implementations", "explain how requests flow through the pipeline"

        - **complex**: Deep reasoning, multi-hop analysis, code generation, refactoring, planning, architecture.
          Examples: "refactor the auth system", "design a caching layer", "plan the migration to microservices"

        ## Response Format

        Respond with ONLY a JSON object:
        {"complexity": "trivial|simple|moderate|complex", "confidence": 0.0-1.0, "reasoning": "brief explanation"}
        """;

    private readonly IModelRouter _modelRouter;
    private readonly ILogger<TaskComplexityClassifier> _logger;

    public TaskComplexityClassifier(
        IModelRouter modelRouter,
        ILogger<TaskComplexityClassifier> logger)
    {
        _modelRouter = modelRouter;
        _logger = logger;
    }

    public async Task<TaskComplexityAssessment> ClassifyAsync(
        AgentTurnContext context,
        CancellationToken ct = default)
    {
        try
        {
            var routingDecision = await _modelRouter.RouteOperationAsync("complexity_classification", ct);
            var client = routingDecision.Client;

            var userPrompt = $"""
                User message: "{context.UserMessage}"
                Turn number: {context.TurnNumber}
                Available tools: {context.AvailableToolCount}
                Recent tools used: {(context.RecentToolNames is { Count: > 0 } tools ? string.Join(", ", tools) : "none")}
                """;

            var messages = new ChatMessage[]
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions
            {
                Temperature = 0.0f,
                MaxOutputTokens = 150
            };

            var response = await client.GetResponseAsync(messages, options, ct);
            var responseText = response.Message.Text?.Trim() ?? string.Empty;

            return ParseResponse(responseText, context.ConversationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM complexity classification failed for conversation {ConversationId}, falling back to Moderate",
                context.ConversationId);
            return FallbackAssessment;
        }
    }

    private TaskComplexityAssessment ParseResponse(string responseText, string conversationId)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var complexityStr = root.GetProperty("complexity").GetString() ?? "moderate";
            var confidence = root.GetProperty("confidence").GetDouble();
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp) ? reasonProp.GetString() : null;

            var complexity = complexityStr.ToLowerInvariant() switch
            {
                "trivial" => TaskComplexity.Trivial,
                "simple" => TaskComplexity.Simple,
                "moderate" => TaskComplexity.Moderate,
                "complex" => TaskComplexity.Complex,
                _ => TaskComplexity.Moderate
            };

            return new TaskComplexityAssessment
            {
                Complexity = complexity,
                Confidence = Math.Clamp(confidence, 0.0, 1.0),
                Source = ClassificationSource.LlmClassifier,
                Reasoning = reasoning
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM classification response for conversation {ConversationId}", conversationId);
            return FallbackAssessment;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~TaskComplexityClassifierTests" -v n`
Expected: All 3 tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Routing/TaskComplexityClassifier.cs src/Content/Tests/Infrastructure.AI.Tests/Routing/TaskComplexityClassifierTests.cs
git commit -m "feat(routing): implement LLM-based TaskComplexityClassifier with few-shot prompting"
```

---

### Task 6: Escalation Tracker Implementation + Tests

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Routing/EscalationTracker.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Routing/EscalationTrackerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.Tests/Routing/EscalationTrackerTests.cs
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.Routing;
using Infrastructure.AI.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Tests.Routing;

public class EscalationTrackerTests
{
    private static readonly IReadOnlyList<ModelTier> Tiers =
    [
        new ModelTier { Name = "economy", ClientType = AIAgentFrameworkClientType.OpenAI, DeploymentName = "gpt-4o-mini", EstimatedCostPer1KTokens = 0.00015m },
        new ModelTier { Name = "standard", ClientType = AIAgentFrameworkClientType.AzureOpenAI, DeploymentName = "gpt-4o", EstimatedCostPer1KTokens = 0.005m },
        new ModelTier { Name = "premium", ClientType = AIAgentFrameworkClientType.AzureOpenAI, DeploymentName = "o3", EstimatedCostPer1KTokens = 0.015m },
    ];

    private readonly EscalationTracker _sut;

    public EscalationTrackerTests()
    {
        var config = new ModelRoutingConfig
        {
            Escalation = new EscalationConfig
            {
                Enabled = true,
                BudgetCeilingPercent = 80,
                CooldownTurns = 2
            }
        };
        _sut = new EscalationTracker(Options.Create(config), NullLogger<EscalationTracker>.Instance);
    }

    [Fact]
    public void GetEffectiveTier_NoEscalation_ReturnsBaseTier()
    {
        var tier = _sut.GetEffectiveTier("conv-1", TaskComplexity.Simple, Tiers);
        Assert.Equal("economy", tier.Name);
    }

    [Fact]
    public void GetEffectiveTier_OneNegativeSignal_BumpsUpOneTier()
    {
        _sut.RecordOutcome("conv-1", TurnOutcome.UserCorrection);

        var tier = _sut.GetEffectiveTier("conv-1", TaskComplexity.Simple, Tiers);
        Assert.Equal("standard", tier.Name);
    }

    [Fact]
    public void GetEffectiveTier_TwoConsecutiveNegatives_BumpsUpTwoTiers()
    {
        _sut.RecordOutcome("conv-1", TurnOutcome.UserCorrection);
        _sut.RecordOutcome("conv-1", TurnOutcome.RetryRequested);

        var tier = _sut.GetEffectiveTier("conv-1", TaskComplexity.Simple, Tiers);
        Assert.Equal("premium", tier.Name);
    }

    [Fact]
    public void GetEffectiveTier_EscalationCappedAtPremium()
    {
        _sut.RecordOutcome("conv-1", TurnOutcome.ToolFailure);
        _sut.RecordOutcome("conv-1", TurnOutcome.ToolFailure);
        _sut.RecordOutcome("conv-1", TurnOutcome.ToolFailure);

        var tier = _sut.GetEffectiveTier("conv-1", TaskComplexity.Complex, Tiers);
        Assert.Equal("premium", tier.Name);
    }

    [Fact]
    public void GetEffectiveTier_SuccessAfterEscalation_StaysEscalatedForCooldown()
    {
        _sut.RecordOutcome("conv-1", TurnOutcome.UserCorrection);
        _sut.RecordOutcome("conv-1", TurnOutcome.Success);

        // Still within cooldown (2 turns)
        var tier = _sut.GetEffectiveTier("conv-1", TaskComplexity.Simple, Tiers);
        Assert.Equal("standard", tier.Name);
    }

    [Fact]
    public void GetEffectiveTier_SuccessAfterCooldownExpires_Downshifts()
    {
        _sut.RecordOutcome("conv-1", TurnOutcome.UserCorrection);
        // Cooldown = 2 turns of success
        _sut.RecordOutcome("conv-1", TurnOutcome.Success);
        _sut.RecordOutcome("conv-1", TurnOutcome.Success);
        _sut.RecordOutcome("conv-1", TurnOutcome.Success);

        var tier = _sut.GetEffectiveTier("conv-1", TaskComplexity.Simple, Tiers);
        Assert.Equal("economy", tier.Name);
    }

    [Fact]
    public void Reset_ClearsEscalationState()
    {
        _sut.RecordOutcome("conv-1", TurnOutcome.UserCorrection);
        _sut.RecordOutcome("conv-1", TurnOutcome.UserCorrection);
        _sut.Reset("conv-1");

        var tier = _sut.GetEffectiveTier("conv-1", TaskComplexity.Simple, Tiers);
        Assert.Equal("economy", tier.Name);
    }

    [Fact]
    public void GetEffectiveTier_IndependentConversations()
    {
        _sut.RecordOutcome("conv-1", TurnOutcome.UserCorrection);

        var tier1 = _sut.GetEffectiveTier("conv-1", TaskComplexity.Simple, Tiers);
        var tier2 = _sut.GetEffectiveTier("conv-2", TaskComplexity.Simple, Tiers);

        Assert.Equal("standard", tier1.Name);
        Assert.Equal("economy", tier2.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~EscalationTrackerTests" -v n`
Expected: FAIL — `EscalationTracker` does not exist yet

- [ ] **Step 3: Implement EscalationTracker**

```csharp
// src/Content/Infrastructure/Infrastructure.AI/Routing/EscalationTracker.cs
using System.Collections.Concurrent;
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Routing;

/// <summary>
/// In-memory per-conversation escalation state machine.
/// Tracks quality signals and adjusts effective model tier accordingly.
/// </summary>
public sealed class EscalationTracker : IEscalationTracker
{
    private readonly ConcurrentDictionary<string, ConversationEscalationState> _states = new();
    private readonly ModelRoutingConfig _config;
    private readonly ILogger<EscalationTracker> _logger;

    public EscalationTracker(
        IOptions<ModelRoutingConfig> config,
        ILogger<EscalationTracker> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public ModelTier GetEffectiveTier(
        string conversationId,
        TaskComplexity baseComplexity,
        IReadOnlyList<ModelTier> availableTiers)
    {
        var orderedTiers = availableTiers.OrderBy(t => t.EstimatedCostPer1KTokens).ToList();
        var baseTierIndex = GetTierIndexForComplexity(baseComplexity, orderedTiers);

        if (!_config.Escalation.Enabled || !_states.TryGetValue(conversationId, out var state))
            return orderedTiers[baseTierIndex];

        var effectiveIndex = Math.Min(baseTierIndex + state.EscalationLevel, orderedTiers.Count - 1);
        return orderedTiers[effectiveIndex];
    }

    public void RecordOutcome(string conversationId, TurnOutcome outcome)
    {
        var state = _states.GetOrAdd(conversationId, _ => new ConversationEscalationState());

        if (IsNegativeOutcome(outcome))
        {
            state.ConsecutiveNegatives++;
            state.EscalationLevel = Math.Min(state.ConsecutiveNegatives, 2);
            state.SuccessesSinceEscalation = 0;

            _logger.LogDebug(
                "Escalation bump for {ConversationId}: {Outcome}, level now {Level}",
                conversationId, outcome, state.EscalationLevel);
        }
        else if (outcome == TurnOutcome.Success && state.EscalationLevel > 0)
        {
            state.ConsecutiveNegatives = 0;
            state.SuccessesSinceEscalation++;

            if (state.SuccessesSinceEscalation > _config.Escalation.CooldownTurns)
            {
                state.EscalationLevel = Math.Max(state.EscalationLevel - 1, 0);
                state.SuccessesSinceEscalation = 0;

                _logger.LogDebug(
                    "Escalation downshift for {ConversationId}: level now {Level}",
                    conversationId, state.EscalationLevel);
            }
        }
        else if (outcome == TurnOutcome.Success)
        {
            state.ConsecutiveNegatives = 0;
        }
    }

    public void Reset(string conversationId)
    {
        _states.TryRemove(conversationId, out _);
    }

    private static int GetTierIndexForComplexity(TaskComplexity complexity, IReadOnlyList<ModelTier> orderedTiers)
    {
        if (orderedTiers.Count == 0) return 0;

        return complexity switch
        {
            TaskComplexity.Trivial => 0,
            TaskComplexity.Simple => 0,
            TaskComplexity.Moderate => Math.Min(1, orderedTiers.Count - 1),
            TaskComplexity.Complex => Math.Min(2, orderedTiers.Count - 1),
            _ => Math.Min(1, orderedTiers.Count - 1)
        };
    }

    private static bool IsNegativeOutcome(TurnOutcome outcome) =>
        outcome is TurnOutcome.UserCorrection or TurnOutcome.RetryRequested
            or TurnOutcome.ToolFailure or TurnOutcome.Timeout;

    private sealed class ConversationEscalationState
    {
        public int EscalationLevel { get; set; }
        public int ConsecutiveNegatives { get; set; }
        public int SuccessesSinceEscalation { get; set; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~EscalationTrackerTests" -v n`
Expected: All 8 tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Routing/EscalationTracker.cs src/Content/Tests/Infrastructure.AI.Tests/Routing/EscalationTrackerTests.cs
git commit -m "feat(routing): implement EscalationTracker with per-conversation tier adjustment"
```

---

### Task 7: ModelRouter Implementation + Tests

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Routing/ModelRouter.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Routing/ModelRouterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.Tests/Routing/ModelRouterTests.cs
using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.Routing;
using Infrastructure.AI.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.Tests.Routing;

public class ModelRouterTests
{
    private readonly Mock<ITaskComplexityHeuristic> _mockHeuristic = new();
    private readonly Mock<ITaskComplexityClassifier> _mockClassifier = new();
    private readonly Mock<IEscalationTracker> _mockEscalation = new();
    private readonly Mock<IChatClientFactory> _mockClientFactory = new();
    private readonly Mock<IChatClient> _mockClient = new();
    private readonly ModelRouter _sut;
    private readonly ModelRoutingConfig _config;

    public ModelRouterTests()
    {
        _config = new ModelRoutingConfig
        {
            Enabled = true,
            DefaultTier = "standard",
            HeuristicConfidenceThreshold = 0.8,
            Tiers =
            [
                new ModelRoutingTierConfig { Name = "economy", ClientType = AIAgentFrameworkClientType.OpenAI, DeploymentName = "gpt-4o-mini", EstimatedCostPer1KTokens = 0.00015m },
                new ModelRoutingTierConfig { Name = "standard", ClientType = AIAgentFrameworkClientType.AzureOpenAI, DeploymentName = "gpt-4o", EstimatedCostPer1KTokens = 0.005m },
                new ModelRoutingTierConfig { Name = "premium", ClientType = AIAgentFrameworkClientType.AzureOpenAI, DeploymentName = "o3", EstimatedCostPer1KTokens = 0.015m },
            ],
            OperationOverrides = new Dictionary<string, string>
            {
                ["raptor_summarization"] = "economy",
                ["crag_evaluation"] = "standard"
            }
        };

        _mockClientFactory
            .Setup(f => f.GetChatClientAsync(It.IsAny<AIAgentFrameworkClientType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockClient.Object);

        _sut = new ModelRouter(
            _mockHeuristic.Object,
            _mockClassifier.Object,
            _mockEscalation.Object,
            _mockClientFactory.Object,
            Options.Create(_config),
            NullLogger<ModelRouter>.Instance);
    }

    [Fact]
    public async Task RouteAgentTurnAsync_HeuristicConfident_SkipsLlm()
    {
        var assessment = new TaskComplexityAssessment
        {
            Complexity = TaskComplexity.Trivial,
            Confidence = 0.95,
            Source = ClassificationSource.Heuristic
        };
        _mockHeuristic.Setup(h => h.Classify(It.IsAny<AgentTurnContext>())).Returns(assessment);

        var economyTier = new ModelTier
        {
            Name = "economy",
            ClientType = AIAgentFrameworkClientType.OpenAI,
            DeploymentName = "gpt-4o-mini",
            EstimatedCostPer1KTokens = 0.00015m
        };
        _mockEscalation
            .Setup(e => e.GetEffectiveTier(It.IsAny<string>(), TaskComplexity.Trivial, It.IsAny<IReadOnlyList<ModelTier>>()))
            .Returns(economyTier);

        var context = new AgentTurnContext { ConversationId = "test", UserMessage = "hi", TurnNumber = 1 };
        var result = await _sut.RouteAgentTurnAsync(context);

        Assert.Equal(TaskComplexity.Trivial, result.Complexity);
        Assert.Equal(ClassificationSource.Heuristic, result.Source);
        Assert.Equal("economy", result.SelectedTier.Name);
        _mockClassifier.Verify(c => c.ClassifyAsync(It.IsAny<AgentTurnContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RouteAgentTurnAsync_HeuristicNull_FallsBackToLlm()
    {
        _mockHeuristic.Setup(h => h.Classify(It.IsAny<AgentTurnContext>())).Returns((TaskComplexityAssessment?)null);

        var llmAssessment = new TaskComplexityAssessment
        {
            Complexity = TaskComplexity.Moderate,
            Confidence = 0.85,
            Source = ClassificationSource.LlmClassifier
        };
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<AgentTurnContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmAssessment);

        var standardTier = new ModelTier
        {
            Name = "standard",
            ClientType = AIAgentFrameworkClientType.AzureOpenAI,
            DeploymentName = "gpt-4o",
            EstimatedCostPer1KTokens = 0.005m
        };
        _mockEscalation
            .Setup(e => e.GetEffectiveTier(It.IsAny<string>(), TaskComplexity.Moderate, It.IsAny<IReadOnlyList<ModelTier>>()))
            .Returns(standardTier);

        var context = new AgentTurnContext { ConversationId = "test", UserMessage = "analyze this", TurnNumber = 3, AvailableToolCount = 4 };
        var result = await _sut.RouteAgentTurnAsync(context);

        Assert.Equal(TaskComplexity.Moderate, result.Complexity);
        Assert.Equal(ClassificationSource.LlmClassifier, result.Source);
        Assert.Equal("standard", result.SelectedTier.Name);
        _mockClassifier.Verify(c => c.ClassifyAsync(It.IsAny<AgentTurnContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RouteOperationAsync_KnownOperation_ReturnsConfiguredTier()
    {
        var result = await _sut.RouteOperationAsync("raptor_summarization");

        Assert.Equal("economy", result.SelectedTier.Name);
        _mockClientFactory.Verify(f => f.GetChatClientAsync(AIAgentFrameworkClientType.OpenAI, "gpt-4o-mini", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RouteOperationAsync_UnknownOperation_ReturnsDefaultTier()
    {
        var result = await _sut.RouteOperationAsync("unknown_operation");

        Assert.Equal("standard", result.SelectedTier.Name);
    }

    [Fact]
    public void ReportTurnOutcome_DelegatesToEscalationTracker()
    {
        _sut.ReportTurnOutcome("conv-1", TurnOutcome.UserCorrection);

        _mockEscalation.Verify(e => e.RecordOutcome("conv-1", TurnOutcome.UserCorrection), Times.Once);
    }

    [Fact]
    public async Task RouteAgentTurnAsync_RoutingDisabled_ReturnsDefaultTier()
    {
        var disabledConfig = _config with { };
        // Create a new router with Enabled=false
        var config = new ModelRoutingConfig
        {
            Enabled = false,
            DefaultTier = "standard",
            Tiers = _config.Tiers
        };

        var sut = new ModelRouter(
            _mockHeuristic.Object,
            _mockClassifier.Object,
            _mockEscalation.Object,
            _mockClientFactory.Object,
            Options.Create(config),
            NullLogger<ModelRouter>.Instance);

        var context = new AgentTurnContext { ConversationId = "test", UserMessage = "complex task", TurnNumber = 5, AvailableToolCount = 10 };
        var result = await sut.RouteAgentTurnAsync(context);

        Assert.Equal("standard", result.SelectedTier.Name);
        _mockHeuristic.Verify(h => h.Classify(It.IsAny<AgentTurnContext>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~ModelRouterTests" -v n`
Expected: FAIL — `ModelRouter` does not exist yet

- [ ] **Step 3: Implement ModelRouter**

```csharp
// src/Content/Infrastructure/Infrastructure.AI/Routing/ModelRouter.cs
using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Routing;

/// <summary>
/// Unified model router orchestrating heuristic classification, LLM fallback,
/// escalation tracking, and client resolution for all model selection decisions.
/// </summary>
public sealed class ModelRouter : IModelRouter
{
    private readonly ITaskComplexityHeuristic _heuristic;
    private readonly ITaskComplexityClassifier _classifier;
    private readonly IEscalationTracker _escalationTracker;
    private readonly IChatClientFactory _clientFactory;
    private readonly ModelRoutingConfig _config;
    private readonly IReadOnlyList<ModelTier> _orderedTiers;
    private readonly ILogger<ModelRouter> _logger;

    public ModelRouter(
        ITaskComplexityHeuristic heuristic,
        ITaskComplexityClassifier classifier,
        IEscalationTracker escalationTracker,
        IChatClientFactory clientFactory,
        IOptions<ModelRoutingConfig> config,
        ILogger<ModelRouter> logger)
    {
        _heuristic = heuristic;
        _classifier = classifier;
        _escalationTracker = escalationTracker;
        _clientFactory = clientFactory;
        _config = config.Value;
        _logger = logger;

        _orderedTiers = _config.Tiers
            .OrderBy(t => t.EstimatedCostPer1KTokens)
            .Select(t => new ModelTier
            {
                Name = t.Name,
                ClientType = t.ClientType,
                DeploymentName = t.DeploymentName,
                FallbackChainName = t.FallbackChainName,
                MaxTokensPerMinute = t.MaxTokensPerMinute,
                EstimatedCostPer1KTokens = t.EstimatedCostPer1KTokens
            })
            .ToList();
    }

    public async Task<ModelRoutingDecision> RouteAgentTurnAsync(
        AgentTurnContext turnContext,
        CancellationToken ct = default)
    {
        if (!_config.Enabled)
            return await BuildDecisionForTierAsync(GetDefaultTier(), TaskComplexity.Moderate, ClassificationSource.Heuristic, 1.0, "Routing disabled", false, ct);

        var assessment = _heuristic.Classify(turnContext);
        if (assessment is null)
        {
            assessment = await _classifier.ClassifyAsync(turnContext, ct);
        }

        var effectiveTier = _escalationTracker.GetEffectiveTier(turnContext.ConversationId, assessment.Complexity, _orderedTiers);
        var wasEscalated = effectiveTier.Name != GetBaseTierForComplexity(assessment.Complexity).Name;

        return await BuildDecisionForTierAsync(effectiveTier, assessment.Complexity, assessment.Source, assessment.Confidence, assessment.Reasoning, wasEscalated, ct);
    }

    public async Task<ModelRoutingDecision> RouteOperationAsync(
        string operationName,
        CancellationToken ct = default)
    {
        var tierName = _config.OperationOverrides.TryGetValue(operationName, out var overrideTier)
            ? overrideTier
            : _config.DefaultTier;

        var tier = _orderedTiers.FirstOrDefault(t => t.Name.Equals(tierName, StringComparison.OrdinalIgnoreCase))
            ?? GetDefaultTier();

        _logger.LogDebug("Routing operation {Operation} to tier {Tier}", operationName, tier.Name);

        return await BuildDecisionForTierAsync(tier, TaskComplexity.Moderate, ClassificationSource.Heuristic, 1.0, $"Operation override: {operationName}", false, ct);
    }

    public async Task<TaskComplexityAssessment> AssessTaskComplexityAsync(
        string taskDescription,
        IReadOnlyList<string> requiredCapabilities,
        CancellationToken ct = default)
    {
        var context = new AgentTurnContext
        {
            ConversationId = "supervisor-assessment",
            UserMessage = taskDescription,
            TurnNumber = 1,
            AvailableToolCount = requiredCapabilities.Count,
            RecentToolNames = requiredCapabilities
        };

        var assessment = _heuristic.Classify(context);
        if (assessment is not null) return assessment;

        return await _classifier.ClassifyAsync(context, ct);
    }

    public void ReportTurnOutcome(string conversationId, TurnOutcome outcome)
    {
        _escalationTracker.RecordOutcome(conversationId, outcome);
    }

    private ModelTier GetDefaultTier() =>
        _orderedTiers.FirstOrDefault(t => t.Name.Equals(_config.DefaultTier, StringComparison.OrdinalIgnoreCase))
        ?? _orderedTiers.First();

    private ModelTier GetBaseTierForComplexity(TaskComplexity complexity)
    {
        var index = complexity switch
        {
            TaskComplexity.Trivial => 0,
            TaskComplexity.Simple => 0,
            TaskComplexity.Moderate => Math.Min(1, _orderedTiers.Count - 1),
            TaskComplexity.Complex => Math.Min(2, _orderedTiers.Count - 1),
            _ => Math.Min(1, _orderedTiers.Count - 1)
        };
        return _orderedTiers[index];
    }

    private async Task<ModelRoutingDecision> BuildDecisionForTierAsync(
        ModelTier tier,
        TaskComplexity complexity,
        ClassificationSource source,
        double confidence,
        string? reasoning,
        bool wasEscalated,
        CancellationToken ct)
    {
        var client = await _clientFactory.GetChatClientAsync(tier.ClientType, tier.DeploymentName, ct);

        return new ModelRoutingDecision
        {
            SelectedTier = tier,
            Client = client,
            Complexity = complexity,
            Source = source,
            Confidence = confidence,
            Reasoning = reasoning,
            WasEscalated = wasEscalated
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests/Infrastructure.AI.Tests.csproj --filter "FullyQualifiedName~ModelRouterTests" -v n`
Expected: All 6 tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Routing/ModelRouter.cs src/Content/Tests/Infrastructure.AI.Tests/Routing/ModelRouterTests.cs
git commit -m "feat(routing): implement unified ModelRouter orchestrating heuristic, LLM, and escalation"
```

---

### Task 8: DI Registration

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.cs`

- [ ] **Step 1: Read the existing DI file to find the right insertion point**

Read `src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.cs` to locate where other services are registered (near `AddSingleton`/`AddTransient` calls). Look for the method signature `AddAIDependencies` or similar.

- [ ] **Step 2: Add routing service registrations**

Add the following registrations to the appropriate `Add*Dependencies` method. The routing services should be registered as:

```csharp
// Unified Model Routing
services.Configure<ModelRoutingConfig>(
    configuration.GetSection("AI:ModelRouting"));
services.AddSingleton<ITaskComplexityHeuristic, TaskComplexityHeuristic>();
services.AddSingleton<IEscalationTracker, EscalationTracker>();
services.AddSingleton<ITaskComplexityClassifier, TaskComplexityClassifier>();
services.AddSingleton<IModelRouter, ModelRouter>();
```

Add the required using directives:

```csharp
using Application.AI.Common.Interfaces.Routing;
using Domain.Common.Config.AI.Routing;
using Infrastructure.AI.Routing;
```

**Note:** `ITaskComplexityClassifier` depends on `IModelRouter` (to get the economy-tier client for classification). `ModelRouter` depends on `ITaskComplexityClassifier`. This is a circular dependency. To break it: register `ITaskComplexityClassifier` as `Lazy<ITaskComplexityClassifier>` in `ModelRouter`, OR use `IServiceProvider` to resolve `ITaskComplexityClassifier` lazily in `ModelRouter`. The simplest approach is to have `ModelRouter` accept `IServiceProvider` and resolve `ITaskComplexityClassifier` on first use.

**Update `ModelRouter` constructor** to accept `IServiceProvider` instead of `ITaskComplexityClassifier` directly:

```csharp
// In ModelRouter constructor:
private ITaskComplexityClassifier? _classifierInstance;
private readonly IServiceProvider _serviceProvider;

// Replace _classifier field with lazy resolution:
private ITaskComplexityClassifier Classifier =>
    _classifierInstance ??= _serviceProvider.GetRequiredService<ITaskComplexityClassifier>();
```

- [ ] **Step 3: Build to verify registration compiles**

Run: `dotnet build src/Content/Infrastructure/Infrastructure.AI/Infrastructure.AI.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.cs src/Content/Infrastructure/Infrastructure.AI/Routing/ModelRouter.cs
git commit -m "feat(routing): register unified model routing services in DI"
```

---

### Task 9: RAG Pipeline Migration

**Files:**
- Modify: 8+ files in `Infrastructure.AI.RAG/` that reference `IRagModelRouter`
- Modify: 2+ files in `Infrastructure.AI.KnowledgeGraph/` that reference `IRagModelRouter`
- Modify: `Infrastructure.AI.RAG/DependencyInjection.cs` (remove old registrations)
- Modify: `Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs` (ComplexityClassification → TaskComplexityAssessment)
- Modify: `Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs` (IQueryComplexityClassifier → ITaskComplexityClassifier)
- Modify: Files using `QueryComplexity` and `ComplexityClassification`

This is the largest task — a mechanical find-and-replace migration across ~15 files.

- [ ] **Step 1: Migrate IRagModelRouter → IModelRouter in all RAG consumers**

For each file that injects `IRagModelRouter`, replace:
1. The `using` directive: `using Application.AI.Common.Interfaces.RAG;` → add `using Application.AI.Common.Interfaces.Routing;`
2. The field type: `IRagModelRouter` → `IModelRouter`
3. The constructor parameter type: `IRagModelRouter` → `IModelRouter`
4. Method calls: `_ragModelRouter.GetClientForOperation("name")` → `(await _modelRouter.RouteOperationAsync("name")).Client`

**Files to update (IRagModelRouter → IModelRouter):**
- `Infrastructure.AI.RAG/Ingestion/ExtractEntitiesExecutor.cs`
- `Infrastructure.AI.RAG/Evaluation/CragEvaluator.cs`
- `Infrastructure.AI.RAG/Evaluation/AnswerFaithfulnessEvaluator.cs`
- `Infrastructure.AI.RAG/Evaluation/RetrievalQualityEvaluator.cs`
- `Infrastructure.AI.RAG/Evaluation/SufficiencyEvaluator.cs`
- `Infrastructure.AI.KnowledgeGraph/Ingestion/KgIngestionWorkflow.cs`
- `Infrastructure.AI.KnowledgeGraph/Feedback/LlmFeedbackDetector.cs`

**Pattern for each file:**

Before:
```csharp
private readonly IRagModelRouter _modelRouter;
// ...
var client = _modelRouter.GetClientForOperation("operation_name");
```

After:
```csharp
private readonly IModelRouter _modelRouter;
// ...
var routingDecision = await _modelRouter.RouteOperationAsync("operation_name", cancellationToken);
var client = routingDecision.Client;
```

**Note:** `GetClientForOperation` was synchronous. `RouteOperationAsync` is async. This means calling methods may need to become async if they aren't already, or the call needs `GetAwaiter().GetResult()` in synchronous contexts. Check each file — most RAG operations are already async.

- [ ] **Step 2: Migrate QueryComplexity → TaskComplexity**

Replace all references to `QueryComplexity` with `TaskComplexity` and update the using directives from `Domain.AI.RAG.Enums` to `Domain.AI.Routing.Enums`.

**Files to update:**
- `Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs`
- `Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs`
- `Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs`
- `Infrastructure.AI.RAG/Retrieval/VectorRetrievalSource.cs`
- `Infrastructure.AI.RAG/Retrieval/GraphRetrievalSource.cs`
- `Application.AI.Common/Interfaces/RAG/IMultiSourceOrchestrator.cs`
- `Application.AI.Common/Interfaces/RAG/IRetrievalSource.cs`
- `Application.AI.Common/Interfaces/RAG/IRetrievalDecisionGate.cs`

- [ ] **Step 3: Migrate ComplexityClassification → TaskComplexityAssessment**

Replace `ComplexityClassification` with `TaskComplexityAssessment` in:
- `IRetrievalDecisionGate.cs` — method parameter type
- `RetrievalDecisionGate.cs` — method parameter type and field references
- `RagOrchestrator.cs` — variable types
- All test files that construct `ComplexityClassification` instances

The property mapping:
- `ComplexityClassification.Complexity` (QueryComplexity) → `TaskComplexityAssessment.Complexity` (TaskComplexity) — same values, different enum type
- `ComplexityClassification.Confidence` → `TaskComplexityAssessment.Confidence` — same
- `ComplexityClassification.Reasoning` → `TaskComplexityAssessment.Reasoning` — same
- `ComplexityClassification.SkipRetrieval` → `TaskComplexityAssessment.SkipRetrieval` — same computed property

- [ ] **Step 4: Migrate IQueryComplexityClassifier → ITaskComplexityClassifier in RagOrchestrator**

In `RagOrchestrator.cs`, the `IQueryComplexityClassifier` injection changes to `ITaskComplexityClassifier`. The method call changes from:
```csharp
_complexityClassifier.ClassifyAsync(query, ct)
```
to:
```csharp
_complexityClassifier.ClassifyAsync(new AgentTurnContext
{
    ConversationId = "rag-pipeline",
    UserMessage = query,
    TurnNumber = 1
}, ct)
```

- [ ] **Step 5: Update RetrievalDecisionGate config source**

`RetrievalDecisionGate` currently reads from `ComplexityRoutingConfig`. Update it to read from `ModelRoutingConfig.RetrievalDefaults` instead. The property names are the same (`ConfidenceThreshold`, `SimpleTopK`, `ComplexTopK`, `SkipRerankForSimple`, `SkipCragForSimple`).

- [ ] **Step 6: Remove old registrations from Infrastructure.AI.RAG/DependencyInjection.cs**

Remove the `IRagModelRouter` registration (line ~97) and the `IQueryComplexityClassifier` registration (line ~239). Update any `GetRequiredService<IRagModelRouter>()` calls to `GetRequiredService<IModelRouter>()`.

- [ ] **Step 7: Remove old registrations from Infrastructure.AI.KnowledgeGraph/DependencyInjection.cs**

Update the `GetRequiredService<IRagModelRouter>()` call (line ~81) to `GetRequiredService<IModelRouter>()`.

- [ ] **Step 8: Build to verify all migrations compile**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor(routing): migrate RAG pipeline from IRagModelRouter to IModelRouter

Replaces IRagModelRouter with IModelRouter.RouteOperationAsync() across all RAG consumers.
Migrates QueryComplexity → TaskComplexity, ComplexityClassification → TaskComplexityAssessment.
Updates RetrievalDecisionGate to use ModelRoutingConfig.RetrievalDefaults."
```

---

### Task 10: AgentFactory Integration

**Files:**
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs`

- [ ] **Step 1: Read AgentFactory to find the model selection code**

Read `src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs` to locate the section where `deploymentOrAgentId` is resolved (around lines 86-97).

- [ ] **Step 2: Add optional IModelRouter dependency**

Add `IModelRouter?` as an optional constructor parameter. When present and `ModelRoutingConfig.Enabled`, the factory can expose a method or property that consumers use for routing-aware agent creation. Since `AgentFactory.CreateAgentAsync()` doesn't know the user message at creation time, the integration point is actually where the agent's `IChatClient` is used — not at factory construction.

The cleanest integration is to add a method:

```csharp
/// <summary>
/// Gets a routing-aware IChatClient for a specific agent turn.
/// Falls back to the agent's configured deployment if routing is disabled.
/// </summary>
public async Task<IChatClient> GetRoutedChatClientAsync(
    AgentTurnContext turnContext,
    string? fallbackDeployment = null,
    CancellationToken ct = default)
{
    if (_modelRouter is not null)
    {
        var decision = await _modelRouter.RouteAgentTurnAsync(turnContext, ct);
        return decision.Client;
    }

    var deployment = fallbackDeployment
        ?? _appConfig.CurrentValue.AI?.AgentFramework?.DefaultDeployment
        ?? "default";
    var clientType = _appConfig.CurrentValue.AI?.AgentFramework?.ClientType
        ?? AIAgentFrameworkClientType.AzureOpenAI;
    return await _chatClientFactory.GetChatClientAsync(clientType, deployment, ct);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Content/Application/Application.AI.Common/Application.AI.Common.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs
git commit -m "feat(routing): add IModelRouter integration to AgentFactory for turn-level routing"
```

---

### Task 11: Supervisor Integration

**Files:**
- Modify: `src/Content/Domain/Domain.AI/Orchestration/SupervisorDecisionContext.cs`
- Modify: `src/Content/Infrastructure/Infrastructure.AI/Agents/CapabilityMatchSupervisor.cs`

- [ ] **Step 1: Add TaskComplexityAssessment to SupervisorDecisionContext**

```csharp
// Add to SupervisorDecisionContext record:
/// <summary>Optional complexity assessment for model-tier-aware agent selection.</summary>
public TaskComplexityAssessment? ComplexityAssessment { get; init; }
```

Add the using directive: `using Domain.AI.Routing.Models;`

- [ ] **Step 2: Update CapabilityMatchSupervisor to use complexity**

Read `CapabilityMatchSupervisor.cs` to find `SelectAgent` or `DelegateAsync`. In the agent selection logic, when `ComplexityAssessment` is present, prefer agents whose configured model tier matches the assessed complexity. If no tier-matched agent is available, fall back to capability-only matching (existing behavior).

This is a soft preference, not a hard filter — add it as a scoring bonus in the selection logic, not a filter.

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Content/Domain/Domain.AI/Orchestration/SupervisorDecisionContext.cs src/Content/Infrastructure/Infrastructure.AI/Agents/CapabilityMatchSupervisor.cs
git commit -m "feat(routing): add complexity-aware agent selection to CapabilityMatchSupervisor"
```

---

### Task 12: Remove Old Types

**Files:**
- Delete: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRagModelRouter.cs`
- Delete: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryComplexityClassifier.cs`
- Delete: `src/Content/Infrastructure/Infrastructure.AI.RAG/Ingestion/RagModelRouter.cs`
- Delete: `src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryComplexityClassifier.cs`
- Delete: `src/Content/Domain/Domain.AI/RAG/Enums/QueryComplexity.cs`
- Delete: `src/Content/Domain/Domain.AI/RAG/Models/ComplexityClassification.cs`
- Delete: `src/Content/Domain/Domain.Common/Config/AI/RAG/ModelTieringConfig.cs`
- Delete: `src/Content/Domain/Domain.Common/Config/AI/RAG/ComplexityRoutingConfig.cs`
- Delete: `src/Content/Domain/Domain.Common/Config/AI/RAG/ModelTierDefinition.cs`

- [ ] **Step 1: Delete old interface files**

```bash
rm src/Content/Application/Application.AI.Common/Interfaces/RAG/IRagModelRouter.cs
rm src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryComplexityClassifier.cs
```

- [ ] **Step 2: Delete old implementation files**

```bash
rm src/Content/Infrastructure/Infrastructure.AI.RAG/Ingestion/RagModelRouter.cs
rm src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryComplexityClassifier.cs
```

- [ ] **Step 3: Delete old domain model files**

```bash
rm src/Content/Domain/Domain.AI/RAG/Enums/QueryComplexity.cs
rm src/Content/Domain/Domain.AI/RAG/Models/ComplexityClassification.cs
rm src/Content/Domain/Domain.Common/Config/AI/RAG/ModelTieringConfig.cs
rm src/Content/Domain/Domain.Common/Config/AI/RAG/ComplexityRoutingConfig.cs
rm src/Content/Domain/Domain.Common/Config/AI/RAG/ModelTierDefinition.cs
```

- [ ] **Step 4: Build to verify no dangling references**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeded, 0 errors. If any `using` directives or references to deleted types remain, fix them now.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(routing): remove replaced types (IRagModelRouter, QueryComplexity, ModelTieringConfig, etc.)

These types have been superseded by:
- IRagModelRouter → IModelRouter
- IQueryComplexityClassifier → ITaskComplexityClassifier
- QueryComplexity → TaskComplexity
- ComplexityClassification → TaskComplexityAssessment
- ModelTieringConfig → ModelRoutingConfig
- ComplexityRoutingConfig → ModelRoutingConfig.RetrievalDefaults"
```

---

### Task 13: Test Migration

**Files:**
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/` (6+ test files)
- Modify: `src/Content/Tests/Infrastructure.AI.Tests/` (if any reference old types)

- [ ] **Step 1: Update test mocks and helpers**

In each test file that references `IRagModelRouter`, `IQueryComplexityClassifier`, `QueryComplexity`, or `ComplexityClassification`:

1. Update `using` directives to point to `Domain.AI.Routing.Enums`, `Domain.AI.Routing.Models`, `Application.AI.Common.Interfaces.Routing`
2. Replace mock types: `Mock<IRagModelRouter>` → `Mock<IModelRouter>`, `Mock<IQueryComplexityClassifier>` → `Mock<ITaskComplexityClassifier>`
3. Replace enum values: `QueryComplexity.Simple` → `TaskComplexity.Simple`
4. Replace record construction: `new ComplexityClassification { ... }` → `new TaskComplexityAssessment { ... }`
5. Update mock setups: `.GetClientForOperation(...)` → `.RouteOperationAsync(...)`

**Test files to update:**
- `ComplexityRoutingIntegrationTests.cs`
- `FullAutonomyIntegrationTests.cs`
- `MultiHopIntegrationTests.cs`
- `RagOrchestratorMultiHopTests.cs`
- `RagOrchestratorTests.cs`
- `RetrievalPlanStepExecutorTests.cs`
- `RetrievalDecisionGateTests.cs`
- Any test file in `Infrastructure.AI.RAG.Tests/` or `Application.AI.Common.Tests/` that uses `RagTestData` factory methods for `ComplexityClassification`

Also update `RagTestData.cs` factory methods if they exist for `ComplexityClassification`.

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass (same count as before migration, minus any tests that tested the old `RagModelRouter` directly — those are replaced by `ModelRouterTests`)

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test(routing): migrate test mocks from IRagModelRouter to IModelRouter"
```

---

### Task 14: Full Build Verification

- [ ] **Step 1: Clean build**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass. Note the total count — it should be the previous count (4,803) plus ~20 new routing tests, minus any removed tests for old types.

- [ ] **Step 3: Verify no references to old types remain**

Search for any remaining references to the removed types:

```bash
grep -r "IRagModelRouter" src/Content/ --include="*.cs"
grep -r "IQueryComplexityClassifier" src/Content/ --include="*.cs"
grep -r "QueryComplexity\b" src/Content/ --include="*.cs"
grep -r "ComplexityClassification\b" src/Content/ --include="*.cs"
grep -r "ModelTieringConfig\b" src/Content/ --include="*.cs"
grep -r "ComplexityRoutingConfig\b" src/Content/ --include="*.cs"
```

Expected: No matches in `src/Content/` (matches in docs/plans are OK).

- [ ] **Step 4: Commit if any cleanup needed**

If any stale references were found in Step 3, fix and commit:

```bash
git add -A
git commit -m "chore(routing): clean up stale references to removed types"
```
