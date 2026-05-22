# Unified Model Router — Design Spec

**Date:** 2026-05-22
**Status:** Approved
**Scope:** Feature 1 of 3 (Model Router → Conversation-Knowledge Bridge → Tool Output Compression)

---

## Goal

Replace the existing `IRagModelRouter` with a unified `IModelRouter` that handles all model selection decisions in the harness: agent turn routing, RAG operation routing, and supervisor delegation advisory. Model selection is driven by task complexity classification with hybrid heuristic + LLM classification and auto-escalation on quality signals.

## Problem Statement

Today:
- `IRagModelRouter` maps RAG operations to economy/standard/premium tiers (cost optimization for retrieval)
- `IQueryComplexityClassifier` classifies queries into Trivial/Simple/Moderate/Complex but only controls retrieval depth, not model choice
- `AgentFactory` always uses one hardcoded deployment (`AppConfig.AI.AgentFramework.DefaultDeployment`)
- `ISupervisor` delegates by tool capabilities only — no awareness of model tier vs task complexity

Result: every agent turn uses the same model regardless of whether the user said "hi" or asked for a complex architectural refactor. Simple turns waste premium model capacity; complex tasks may underperform on economy models.

## Architecture

### Core Principle

One interface (`IModelRouter`) owns all model selection. Three consumers:

1. **Agent turns** — classify turn complexity → select tier-appropriate `IChatClient`
2. **RAG operations** — map operation names to tiers (replaces `IRagModelRouter`)
3. **Supervisor delegation** — assess subtask complexity for delegation decisions

### Classification Pipeline

Two-phase classification: fast heuristic first, LLM fallback for ambiguous cases.

```
User message arrives
  → Build AgentTurnContext
  → ITaskComplexityHeuristic.Classify()
     → Confidence ≥ 0.8? Use it.
     → Confidence < 0.8? → ITaskComplexityClassifier.ClassifyAsync() (LLM)
  → IEscalationTracker.GetEffectiveTier() (may bump up based on recent signals)
  → IModelRouter resolves IChatClient from tier config
  → Agent executes turn
  → After turn: IModelRouter.ReportTurnOutcome() (feeds escalation tracker)
```

### Auto-Escalation

Per-conversation quality tracking with automatic tier adjustment:
- Tiers are ordered by `EstimatedCostPer1KTokens` ascending (economy < standard < premium)
- 1 negative signal (UserCorrection, RetryRequested, ToolFailure) → bump up 1 tier
- 2 consecutive negative signals → bump up 2 tiers (cap at premium)
- 1 Success after escalation → stay escalated for 2 more turns, then attempt downshift
- Budget guard: if IBudgetTrackingService reports spend > 80% of session budget, block escalation
- State is in-memory per conversation (same lifetime as IAgentConversationCache)

---

## Domain Models

### TaskComplexity Enum (replaces QueryComplexity)

```csharp
// Domain.AI/Routing/Enums/TaskComplexity.cs
public enum TaskComplexity
{
    /// <summary>Parametric knowledge, simple lookup, greeting, acknowledgment.</summary>
    Trivial,

    /// <summary>Single-step reasoning, basic tool use, straightforward Q&A.</summary>
    Simple,

    /// <summary>Multi-step reasoning, multiple tools, synthesis, comparison.</summary>
    Moderate,

    /// <summary>Deep reasoning, multi-hop, code generation, planning, architectural decisions.</summary>
    Complex
}
```

### ClassificationSource Enum

```csharp
// Domain.AI/Routing/Enums/ClassificationSource.cs
public enum ClassificationSource
{
    /// <summary>Fast heuristic rules (zero LLM cost).</summary>
    Heuristic,

    /// <summary>LLM-based few-shot classification (fallback for ambiguous cases).</summary>
    LlmClassifier
}
```

### TurnOutcome Enum

```csharp
// Domain.AI/Routing/Enums/TurnOutcome.cs
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

### ModelTier Record

```csharp
// Domain.AI/Routing/Models/ModelTier.cs
public sealed record ModelTier
{
    public required string Name { get; init; }
    public required AIAgentFrameworkClientType ClientType { get; init; }
    public required string DeploymentName { get; init; }
    public string? FallbackChainName { get; init; }
    public int MaxTokensPerMinute { get; init; }
    public decimal EstimatedCostPer1KTokens { get; init; }
}
```

### TaskComplexityAssessment Record

```csharp
// Domain.AI/Routing/Models/TaskComplexityAssessment.cs
public sealed record TaskComplexityAssessment
{
    public required TaskComplexity Complexity { get; init; }
    public required double Confidence { get; init; }
    public required ClassificationSource Source { get; init; }
    public string? Reasoning { get; init; }
    public bool SkipRetrieval => Complexity == TaskComplexity.Trivial;
}
```

### ModelRoutingDecision Record

```csharp
// Domain.AI/Routing/Models/ModelRoutingDecision.cs
public sealed record ModelRoutingDecision
{
    public required ModelTier SelectedTier { get; init; }
    public required IChatClient Client { get; init; }
    public required TaskComplexity Complexity { get; init; }
    public required ClassificationSource Source { get; init; }
    public required double Confidence { get; init; }
    public string? Reasoning { get; init; }
    public bool WasEscalated { get; init; }
}
```

### AgentTurnContext Record

```csharp
// Domain.AI/Routing/Models/AgentTurnContext.cs
public sealed record AgentTurnContext
{
    public required string ConversationId { get; init; }
    public required string UserMessage { get; init; }
    public required int TurnNumber { get; init; }
    public int AvailableToolCount { get; init; }
    public int ConversationDepth { get; init; }
    public IReadOnlyList<string>? RecentToolNames { get; init; }
}
```

---

## Interfaces

### IModelRouter (Application.AI.Common — replaces IRagModelRouter)

```csharp
// Application.AI.Common/Interfaces/Routing/IModelRouter.cs
public interface IModelRouter
{
    /// <summary>
    /// Routes an agent conversation turn to the appropriate model tier.
    /// Builds AgentTurnContext, classifies complexity, applies escalation, returns client.
    /// </summary>
    Task<ModelRoutingDecision> RouteAgentTurnAsync(
        AgentTurnContext turnContext,
        CancellationToken ct = default);

    /// <summary>
    /// Routes a named operation (e.g., RAG pipeline step) to its configured model tier.
    /// Replaces IRagModelRouter.GetClientForOperation().
    /// </summary>
    Task<ModelRoutingDecision> RouteOperationAsync(
        string operationName,
        CancellationToken ct = default);

    /// <summary>
    /// Assesses task complexity for supervisor delegation decisions.
    /// Does not return an IChatClient — advisory only.
    /// </summary>
    Task<TaskComplexityAssessment> AssessTaskComplexityAsync(
        string taskDescription,
        IReadOnlyList<string> requiredCapabilities,
        CancellationToken ct = default);

    /// <summary>
    /// Reports turn outcome for escalation tracking.
    /// Call after each agent turn completes.
    /// </summary>
    void ReportTurnOutcome(string conversationId, TurnOutcome outcome);
}
```

### ITaskComplexityHeuristic (Application.AI.Common)

```csharp
// Application.AI.Common/Interfaces/Routing/ITaskComplexityHeuristic.cs
public interface ITaskComplexityHeuristic
{
    /// <summary>
    /// Fast, zero-cost heuristic classification.
    /// Returns null when confidence is below threshold (triggers LLM fallback).
    /// </summary>
    TaskComplexityAssessment? Classify(AgentTurnContext context);
}
```

### ITaskComplexityClassifier (Application.AI.Common — replaces IQueryComplexityClassifier)

```csharp
// Application.AI.Common/Interfaces/Routing/ITaskComplexityClassifier.cs
public interface ITaskComplexityClassifier
{
    /// <summary>
    /// LLM-based few-shot complexity classification. Used as fallback when heuristic is not confident.
    /// Uses economy-tier model for classification (the classifier itself is cheap).
    /// </summary>
    Task<TaskComplexityAssessment> ClassifyAsync(
        AgentTurnContext context,
        CancellationToken ct = default);
}
```

### IEscalationTracker (Application.AI.Common)

```csharp
// Application.AI.Common/Interfaces/Routing/IEscalationTracker.cs
public interface IEscalationTracker
{
    /// <summary>
    /// Returns the effective tier for a conversation, factoring in recent quality signals.
    /// May return a higher tier than baseComplexity warrants if escalation is active.
    /// </summary>
    ModelTier GetEffectiveTier(
        string conversationId,
        TaskComplexity baseComplexity,
        IReadOnlyList<ModelTier> availableTiers);

    /// <summary>Records a turn outcome for escalation tracking.</summary>
    void RecordOutcome(string conversationId, TurnOutcome outcome);

    /// <summary>Resets escalation state for a conversation.</summary>
    void Reset(string conversationId);
}
```

---

## Configuration

```json
{
  "AI": {
    "ModelRouting": {
      "Enabled": true,
      "DefaultTier": "standard",
      "HeuristicConfidenceThreshold": 0.8,
      "Tiers": [
        {
          "Name": "economy",
          "ClientType": "OpenAI",
          "DeploymentName": "gpt-4o-mini",
          "FallbackChainName": null,
          "MaxTokensPerMinute": 100000,
          "EstimatedCostPer1KTokens": 0.00015
        },
        {
          "Name": "standard",
          "ClientType": "AzureOpenAI",
          "DeploymentName": "gpt-4o",
          "FallbackChainName": "primary-chain",
          "MaxTokensPerMinute": 80000,
          "EstimatedCostPer1KTokens": 0.005
        },
        {
          "Name": "premium",
          "ClientType": "AzureOpenAI",
          "DeploymentName": "o3",
          "FallbackChainName": "premium-chain",
          "MaxTokensPerMinute": 30000,
          "EstimatedCostPer1KTokens": 0.015
        }
      ],
      "OperationOverrides": {
        "raptor_summarization": "economy",
        "contextual_enrichment": "economy",
        "entity_extraction": "economy",
        "crag_evaluation": "standard",
        "query_classification": "standard",
        "query_transformation": "economy",
        "complexity_classification": "economy"
      },
      "Escalation": {
        "Enabled": true,
        "BudgetCeilingPercent": 80,
        "CooldownTurns": 2
      },
      "HeuristicThresholds": {
        "TrivialMaxLength": 50,
        "SimpleMaxLength": 200,
        "ModerateMaxLength": 1000,
        "ComplexMinToolCount": 8,
        "ComplexKeywords": ["refactor", "design", "plan", "architect", "migrate", "rewrite"],
        "TrivialKeywords": ["hi", "hello", "thanks", "ok", "yes", "no"]
      },
      "RetrievalDefaults": {
        "ConfidenceThreshold": 0.7,
        "SimpleTopK": 5,
        "ComplexTopK": 15,
        "SkipRerankForSimple": true,
        "SkipCragForSimple": true
      }
    }
  }
}
```

Config POCO: `ModelRoutingConfig` in `Domain.Common/Config/AI/Routing/`.

---

## Heuristic Classification Rules

| Signal | Trivial (≥0.9) | Simple (≥0.85) | Moderate (≥0.8) | Complex (≥0.85) |
|--------|-----------------|----------------|------------------|-----------------|
| Message length | <50 chars | <200 chars | <1000 chars | >1000 chars |
| Tool count available | 0 | 1-3 | 4-8 | >8 |
| Contains code block | — | — | ≥1 block | — |
| Turn number | 1 + greeting pattern | 1-3 | 4+ | 8+ with tool chains |
| Keywords | Matches TrivialKeywords | "what is", "show", "list" | "compare", "analyze", "explain" | Matches ComplexKeywords |
| Recent tool results | none | ≤1 | 2-4 | >4 |

Multiple signals combine: each matching signal adds to the confidence for its tier. The tier with the highest aggregate confidence wins. If no tier exceeds `HeuristicConfidenceThreshold`, return null (trigger LLM fallback).

---

## Integration Points

### AgentFactory

`AgentFactory.CreateAgentAsync()` gains an optional `IModelRouter` dependency. When present and `ModelRoutingConfig.Enabled`, uses `RouteAgentTurnAsync()` instead of the hardcoded deployment. When absent or disabled, falls back to existing `DeploymentName ?? DefaultDeployment` path. Zero breaking change for consumers who don't opt in.

### Supervisor

`SupervisorDecisionContext` gains a `TaskComplexityAssessment?` field. `CapabilityMatchSupervisor` calls `IModelRouter.AssessTaskComplexityAsync()` before delegation, then filters candidate agents to prefer those whose configured model tier matches the assessed complexity. If no tier-matched agent is available, falls back to capability-only matching (existing behavior).

### RAG Pipeline

All existing `IRagModelRouter.GetClientForOperation()` calls migrate to `IModelRouter.RouteOperationAsync()`. The `OperationOverrides` config section provides the same operation→tier mapping. `IRetrievalDecisionGate` continues to use complexity for pipeline parameters (TopK, reranking, CRAG) via `RetrievalDefaults` config — now sourced from `ModelRoutingConfig.RetrievalDefaults` instead of `ComplexityRoutingConfig`.

### Budget Integration

`IEscalationTracker` consults `IBudgetTrackingService` before allowing escalation. If current session spend exceeds `Escalation.BudgetCeilingPercent` of the configured budget, escalation is blocked and the router logs a warning.

---

## Migration Plan

### Removed Interfaces
- `IRagModelRouter` → replaced by `IModelRouter.RouteOperationAsync()`
- `IQueryComplexityClassifier` → replaced by `ITaskComplexityClassifier`

### Removed Types
- `QueryComplexity` → replaced by `TaskComplexity`
- `ComplexityClassification` → replaced by `TaskComplexityAssessment`
- `ModelTieringConfig` → merged into `ModelRoutingConfig`
- `ComplexityRoutingConfig` → merged into `ModelRoutingConfig.RetrievalDefaults`

### Migration Steps
1. Create new domain models in `Domain.AI/Routing/`
2. Create new interfaces in `Application.AI.Common/Interfaces/Routing/`
3. Implement `TaskComplexityHeuristic`, `TaskComplexityClassifier`, `EscalationTracker`, `ModelRouter` in `Infrastructure.AI/Routing/`
4. Update `AgentFactory` to use `IModelRouter` (with fallback to existing path)
5. Update `SupervisorDecisionContext` and `CapabilityMatchSupervisor`
6. Migrate RAG consumers from `IRagModelRouter` to `IModelRouter`
7. Remove old interfaces and types
8. Update `DependencyInjection.cs` registrations
9. Update `ModelRoutingConfig` in appsettings

---

## Testing Strategy

- **Heuristic classifier:** Table-driven unit tests (input AgentTurnContext → expected TaskComplexity + confidence). Cover all signal combinations and edge cases.
- **LLM classifier:** Unit test with mock IChatClient verifying prompt construction and response parsing. Integration test with real LLM optional.
- **Escalation tracker:** State machine unit tests — sequence of TurnOutcome inputs → verify tier bumps, cooldowns, downshifts, budget blocks.
- **Model router:** Integration test with mock IChatClientFactory. Verify heuristic→LLM fallback path, operation routing, escalation feedback loop.
- **Migration:** Regression test confirming all existing RAG operation→tier mappings produce identical results through the new interface.
- **Supervisor:** Verify complexity-aware agent selection prefers tier-matched agents.

---

## File Placement

| File | Project | Folder |
|------|---------|--------|
| `TaskComplexity.cs`, `ClassificationSource.cs`, `TurnOutcome.cs` | Domain.AI | `Routing/Enums/` |
| `ModelTier.cs`, `TaskComplexityAssessment.cs`, `ModelRoutingDecision.cs`, `AgentTurnContext.cs` | Domain.AI | `Routing/Models/` |
| `ModelRoutingConfig.cs`, `EscalationConfig.cs`, `HeuristicThresholdsConfig.cs` | Domain.Common | `Config/AI/Routing/` |
| `IModelRouter.cs`, `ITaskComplexityHeuristic.cs`, `ITaskComplexityClassifier.cs`, `IEscalationTracker.cs` | Application.AI.Common | `Interfaces/Routing/` |
| `ModelRouter.cs`, `TaskComplexityHeuristic.cs`, `TaskComplexityClassifier.cs`, `EscalationTracker.cs` | Infrastructure.AI | `Routing/` |
| DI registration | Infrastructure.AI | `DependencyInjection.cs` |
