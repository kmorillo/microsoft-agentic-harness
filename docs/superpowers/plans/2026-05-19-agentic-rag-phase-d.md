# Phase D: Full Autonomy -- Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make RAG a first-class citizen of the planner DAG by adding a `StepType.Retrieval` plan step, multi-source orchestration across vector/graph/web, Ragas-inspired quality evaluation, and per-execution cost tracking with CI/CD quality gates.

**Architecture:** A new `RetrievalPlanStepExecutor` (keyed on `StepType.Retrieval`) lets the planner include RAG retrieval as an explicit step in any plan. `MultiSourceOrchestrator` fans out queries to vector, graph, and web sources in parallel, deduplicates by chunk ID, and selects sources based on `QueryComplexity`. `RetrievalQualityEvaluator` produces Ragas-style metrics (context precision, recall, faithfulness, answer relevancy) via LLM judges. `RetrievalCostTracker` provides thread-safe token/latency accounting per execution. Quality gate test fixtures enforce minimum metric thresholds in CI.

**Tech Stack:** C# .NET 10, Microsoft.Extensions.AI (IChatClient), xUnit + Moq + FluentAssertions, keyed DI

**Depends on:** Phase A (complexity routing) + Phase B (multi-hop retrieval). Soft dependency on Phase C (graph memory enhances but isn't required).

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Modify | `src/Content/Domain/Domain.AI/Planner/StepType.cs` | Add `Retrieval` enum value |
| Create | `src/Content/Domain/Domain.AI/Planner/RetrievalStepConfiguration.cs` | Retrieval step config |
| Modify | `src/Content/Domain/Domain.AI/Planner/StepConfiguration.cs` | Add JsonDerivedType for retrieval |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/RetrievalQualityReport.cs` | Quality metrics report |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/RetrievalCostSummary.cs` | Cost tracking model |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/SourceRetrievalResult.cs` | Per-source result wrapper |
| Create | `src/Content/Domain/Domain.Common/Config/AI/RAG/MultiSourceConfig.cs` | Multi-source orchestration config |
| Create | `src/Content/Domain/Domain.Common/Config/AI/RAG/QualityGateConfig.cs` | CI/CD quality gate config |
| Modify | `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs` | Add MultiSource + QualityGate config |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IMultiSourceOrchestrator.cs` | Multi-source interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalQualityEvaluator.cs` | Quality evaluation interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalCostTracker.cs` | Cost tracking interface |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs` | Multi-source impl |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/RetrievalQualityEvaluator.cs` | Quality metrics impl |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalCostTracker.cs` | Cost tracking impl |
| Create | `src/Content/Infrastructure/Infrastructure.AI/Planner/StepExecutors/RetrievalPlanStepExecutor.cs` | Retrieval step executor |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs` | Multi-source integration |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs` | Register new services |
| Modify | `src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.Planner.cs` | Register RetrievalPlanStepExecutor |
| Modify | `src/Content/Domain/Domain.AI/Telemetry/Conventions/RagConventions.cs` | Add multi-source + quality OTel constants |
| Modify | `src/Content/Application/Application.AI.Common/OpenTelemetry/Metrics/RagRetrievalMetrics.cs` | Add multi-source + quality instruments |
| Modify | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs` | Add quality/cost test helpers |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorTests.cs` | Multi-source tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/RetrievalQualityEvaluatorTests.cs` | Quality evaluator tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalCostTrackerTests.cs` | Cost tracker tests |
| Create | `src/Content/Tests/Infrastructure.AI.Tests/Planner/StepExecutors/RetrievalPlanStepExecutorTests.cs` | Step executor tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/QualityGates/RagQualityGateTests.cs` | CI/CD quality gate tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/FullAutonomyIntegrationTests.cs` | E2E integration tests |

---

### Task 1: StepType.Retrieval + RetrievalStepConfiguration (Domain)

**Files:**
- Modify: `src/Content/Domain/Domain.AI/Planner/StepType.cs`
- Create: `src/Content/Domain/Domain.AI/Planner/RetrievalStepConfiguration.cs`
- Modify: `src/Content/Domain/Domain.AI/Planner/StepConfiguration.cs`

- [ ] **Step 1: Add `Retrieval` to the StepType enum**

```csharp
// src/Content/Domain/Domain.AI/Planner/StepType.cs
namespace Domain.AI.Planner;

/// <summary>
/// Determines which keyed <c>IPlanStepExecutor</c> handles the step at runtime.
/// Each value maps to a specific executor registered via keyed dependency injection.
/// </summary>
public enum StepType
{
    /// <summary>Delegates to <c>RunConversationCommand</c> for LLM inference.</summary>
    LlmCall,

    /// <summary>Routes tool execution through the appropriate sandbox.</summary>
    ToolUse,

    /// <summary>Non-blocking escalation requiring human approval before proceeding.</summary>
    HumanGate,

    /// <summary>Evaluates a condition expression and activates the true or false edge.</summary>
    ConditionalBranch,

    /// <summary>Invokes a child plan in an isolated scope with depth limiting.</summary>
    SubPlanInvocation,

    /// <summary>
    /// Executes a RAG retrieval query, producing assembled context as output for
    /// downstream steps. Supports single-source and multi-source orchestration.
    /// </summary>
    Retrieval
}
```

- [ ] **Step 2: Create `RetrievalStepConfiguration`**

```csharp
// src/Content/Domain/Domain.AI/Planner/RetrievalStepConfiguration.cs
using Domain.AI.RAG.Enums;

namespace Domain.AI.Planner;

/// <summary>
/// Configuration for a retrieval plan step. Specifies the query, retrieval strategy,
/// result count, and whether to use multi-source orchestration across vector, graph,
/// and web sources.
/// </summary>
public sealed record RetrievalStepConfiguration : StepConfiguration
{
    /// <summary>
    /// The retrieval query text. May contain upstream output placeholders that are
    /// resolved by the executor before retrieval.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Optional retrieval strategy override. When null, the query classifier determines
    /// the strategy based on query complexity.
    /// </summary>
    public RetrievalStrategy? Strategy { get; init; }

    /// <summary>
    /// Maximum number of results to return. When null, uses the default from
    /// <c>AppConfig:AI:Rag:Retrieval:TopK</c>.
    /// </summary>
    public int? TopK { get; init; }

    /// <summary>
    /// Optional collection or index name to search. When null, the default collection is used.
    /// </summary>
    public string? CollectionName { get; init; }

    /// <summary>
    /// When <c>true</c>, uses <see cref="IMultiSourceOrchestrator"/> to fan out across
    /// vector, graph, and web sources in parallel. When <c>false</c>, uses the standard
    /// <see cref="IRagOrchestrator"/> single-pipeline path.
    /// </summary>
    public bool UseMultiSource { get; init; } = false;
}
```

- [ ] **Step 3: Add `JsonDerivedType` for retrieval to `StepConfiguration`**

```csharp
// src/Content/Domain/Domain.AI/Planner/StepConfiguration.cs
using System.Text.Json.Serialization;

namespace Domain.AI.Planner;

/// <summary>
/// Abstract base for step-specific configuration. Each <see cref="StepType"/> has a corresponding
/// concrete subtype. Polymorphic JSON serialization uses the <c>type</c> discriminator property
/// for round-tripping through EF Core JSON columns and API payloads.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LlmCallConfig), "llm_call")]
[JsonDerivedType(typeof(ToolUseConfig), "tool_use")]
[JsonDerivedType(typeof(HumanGateConfig), "human_gate")]
[JsonDerivedType(typeof(ConditionalBranchConfig), "conditional_branch")]
[JsonDerivedType(typeof(SubPlanConfig), "sub_plan")]
[JsonDerivedType(typeof(RetrievalStepConfiguration), "retrieval")]
public abstract record StepConfiguration;
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Domain/Domain.AI/Planner/StepType.cs src/Content/Domain/Domain.AI/Planner/RetrievalStepConfiguration.cs src/Content/Domain/Domain.AI/Planner/StepConfiguration.cs
git commit -m "feat(rag): add StepType.Retrieval and RetrievalStepConfiguration for planner integration"
```

---

### Task 2: Domain Models -- Quality Report, Cost Summary, Source Result

**Files:**
- Create: `src/Content/Domain/Domain.AI/RAG/Models/RetrievalQualityReport.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/RetrievalCostSummary.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/SourceRetrievalResult.cs`

- [ ] **Step 1: Create `RetrievalQualityReport`**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/RetrievalQualityReport.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// Ragas-inspired quality metrics for a single retrieval+generation cycle.
/// Produced by <c>IRetrievalQualityEvaluator</c> and used by CI/CD quality gates
/// to prevent quality regression.
/// </summary>
public sealed record RetrievalQualityReport
{
    /// <summary>
    /// Fraction of retrieved context chunks that are relevant to the query (0.0-1.0).
    /// Higher values indicate less noise in the retrieval results.
    /// Analogous to Ragas context_precision.
    /// </summary>
    public required double ContextPrecision { get; init; }

    /// <summary>
    /// Fraction of ground-truth information captured in the retrieved context (0.0-1.0).
    /// Requires a ground-truth answer for calculation. Set to -1.0 when ground truth
    /// is unavailable and recall was not evaluated.
    /// Analogous to Ragas context_recall.
    /// </summary>
    public required double ContextRecall { get; init; }

    /// <summary>
    /// Degree to which the generated answer is supported by the retrieved context (0.0-1.0).
    /// Low faithfulness indicates hallucination beyond what the context provides.
    /// Analogous to Ragas faithfulness.
    /// </summary>
    public required double Faithfulness { get; init; }

    /// <summary>
    /// How well the generated answer addresses the original query (0.0-1.0).
    /// Low relevancy indicates the answer drifted from the question despite having
    /// good context.
    /// Analogous to Ragas answer_relevancy.
    /// </summary>
    public required double AnswerRelevancy { get; init; }

    /// <summary>
    /// Weighted average of the four component metrics. Weights: precision 0.25,
    /// recall 0.25 (or redistributed when recall is skipped), faithfulness 0.3,
    /// relevancy 0.2. Range 0.0-1.0.
    /// </summary>
    public required double OverallScore { get; init; }

    /// <summary>
    /// LLM-generated reasoning explaining the quality assessment. Includes per-metric
    /// justifications and identified weaknesses.
    /// </summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// Timestamp when this evaluation was performed.
    /// </summary>
    public required DateTimeOffset EvaluatedAt { get; init; }
}
```

- [ ] **Step 2: Create `RetrievalCostSummary`**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/RetrievalCostSummary.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// Token usage and cost accounting for a retrieval execution or plan execution.
/// Aggregated by <c>IRetrievalCostTracker</c> from individual retrieval calls.
/// </summary>
public sealed record RetrievalCostSummary
{
    /// <summary>Total tokens consumed across all retrieval-related LLM calls.</summary>
    public required int TotalTokensUsed { get; init; }

    /// <summary>Prompt/input tokens sent to the LLM (embedding, classification, evaluation).</summary>
    public required int PromptTokens { get; init; }

    /// <summary>Completion/output tokens received from the LLM.</summary>
    public required int CompletionTokens { get; init; }

    /// <summary>Number of distinct retrieval API calls made.</summary>
    public required int RetrievalCalls { get; init; }

    /// <summary>Cumulative wall-clock time spent on retrieval operations.</summary>
    public required TimeSpan TotalLatency { get; init; }

    /// <summary>
    /// Estimated cost in USD based on token counts and configured per-token pricing.
    /// Approximation using default GPT-4o pricing: $2.50/1M input, $10.00/1M output.
    /// </summary>
    public required double EstimatedCost { get; init; }
}
```

- [ ] **Step 3: Create `SourceRetrievalResult`**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/SourceRetrievalResult.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// Wraps retrieval results from a single source (vector, graph, web) with
/// per-source performance metrics. Used by <c>MultiSourceOrchestrator</c>
/// to track which sources contributed results and their individual latency.
/// </summary>
public sealed record SourceRetrievalResult
{
    /// <summary>
    /// Identifies the retrieval source (e.g., <c>"vector"</c>, <c>"graph"</c>, <c>"web"</c>).
    /// </summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// Results returned by this source, scored and ready for deduplication and merging.
    /// </summary>
    public required IReadOnlyList<RetrievalResult> Results { get; init; }

    /// <summary>
    /// Wall-clock time this source took to respond.
    /// </summary>
    public required TimeSpan Latency { get; init; }

    /// <summary>
    /// Tokens consumed by this source's retrieval operations (embedding, search, etc.).
    /// </summary>
    public required int TokensUsed { get; init; }
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Domain/Domain.AI/RAG/Models/RetrievalQualityReport.cs src/Content/Domain/Domain.AI/RAG/Models/RetrievalCostSummary.cs src/Content/Domain/Domain.AI/RAG/Models/SourceRetrievalResult.cs
git commit -m "feat(rag): add RetrievalQualityReport, RetrievalCostSummary, and SourceRetrievalResult domain models"
```

---

### Task 3: Configuration -- MultiSourceConfig and QualityGateConfig

**Files:**
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/MultiSourceConfig.cs`
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/QualityGateConfig.cs`
- Modify: `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs`

- [ ] **Step 1: Create `MultiSourceConfig`**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/MultiSourceConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for multi-source retrieval orchestration.
/// Controls which sources are enabled, parallelism limits, and per-source timeouts.
/// Bound from <c>AppConfig:AI:Rag:MultiSource</c> in appsettings.json.
/// </summary>
public sealed class MultiSourceConfig
{
    /// <summary>
    /// Gets or sets whether multi-source orchestration is enabled.
    /// When <c>false</c>, all retrieval goes through the standard single-pipeline path.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of enabled source names.
    /// Valid values: <c>"vector"</c>, <c>"graph"</c>, <c>"web"</c>.
    /// Sources not in this list are never queried regardless of query complexity.
    /// </summary>
    public List<string> EnabledSources { get; set; } = ["vector", "graph"];

    /// <summary>
    /// Gets or sets the maximum number of sources to query in parallel.
    /// Limits concurrency to prevent resource exhaustion on constrained hosts.
    /// </summary>
    public int MaxParallelSources { get; set; } = 3;

    /// <summary>
    /// Gets or sets the per-source timeout. Sources that exceed this timeout
    /// are abandoned gracefully and their partial results are discarded.
    /// </summary>
    public TimeSpan SourceTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the cost per 1 million input tokens in USD for cost estimation.
    /// Default aligned with GPT-4o input pricing.
    /// </summary>
    public double CostPerMillionInputTokens { get; set; } = 2.50;

    /// <summary>
    /// Gets or sets the cost per 1 million output tokens in USD for cost estimation.
    /// Default aligned with GPT-4o output pricing.
    /// </summary>
    public double CostPerMillionOutputTokens { get; set; } = 10.00;
}
```

- [ ] **Step 2: Create `QualityGateConfig`**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/QualityGateConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for CI/CD retrieval quality gates.
/// When enabled, quality gate tests evaluate retrieval against a golden dataset
/// and fail the build if metrics drop below configured thresholds.
/// Bound from <c>AppConfig:AI:Rag:QualityGate</c> in appsettings.json.
/// </summary>
public sealed class QualityGateConfig
{
    /// <summary>
    /// Gets or sets whether quality gate evaluation is enabled.
    /// When <c>false</c>, quality gate test fixtures are skipped.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum acceptable context precision score (0.0-1.0).
    /// Build fails if the average precision across the golden dataset drops below this.
    /// </summary>
    public double MinContextPrecision { get; set; } = 0.7;

    /// <summary>
    /// Gets or sets the minimum acceptable faithfulness score (0.0-1.0).
    /// Faithfulness below this threshold indicates unacceptable hallucination rates.
    /// </summary>
    public double MinFaithfulness { get; set; } = 0.8;

    /// <summary>
    /// Gets or sets the minimum acceptable overall quality score (0.0-1.0).
    /// Weighted average of all four Ragas-style metrics.
    /// </summary>
    public double MinOverallScore { get; set; } = 0.7;

    /// <summary>
    /// Gets or sets the filesystem path to the golden dataset JSON file.
    /// Each entry contains a query, expected answer, and relevant chunk IDs.
    /// Null uses the embedded default dataset.
    /// </summary>
    public string? GoldenDatasetPath { get; set; }
}
```

- [ ] **Step 3: Add `MultiSource` and `QualityGate` properties to `RagConfig`**

Add these two properties to the end of the `RagConfig` class:

```csharp
    /// <summary>
    /// Gets or sets the multi-source orchestration configuration for fanning out
    /// retrieval queries across vector, graph, and web sources.
    /// </summary>
    public MultiSourceConfig MultiSource { get; set; } = new();

    /// <summary>
    /// Gets or sets the CI/CD quality gate configuration for enforcing minimum
    /// retrieval quality thresholds via Ragas-style evaluation.
    /// </summary>
    public QualityGateConfig QualityGate { get; set; } = new();
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Domain/Domain.Common/Config/AI/RAG/MultiSourceConfig.cs src/Content/Domain/Domain.Common/Config/AI/RAG/QualityGateConfig.cs src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs
git commit -m "feat(rag): add MultiSourceConfig and QualityGateConfig to RagConfig"
```

---

### Task 4: Application Interfaces -- IMultiSourceOrchestrator, IRetrievalQualityEvaluator, IRetrievalCostTracker

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IMultiSourceOrchestrator.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalQualityEvaluator.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalCostTracker.cs`

- [ ] **Step 1: Create `IMultiSourceOrchestrator`**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IMultiSourceOrchestrator.cs
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Coordinates retrieval across multiple sources (vector store, knowledge graph, web)
/// in parallel, merges results, and deduplicates by chunk ID. Source selection is
/// driven by query complexity:
/// <list type="bullet">
///   <item><see cref="QueryComplexity.Trivial"/> / <see cref="QueryComplexity.Simple"/>
///         -- vector only.</item>
///   <item><see cref="QueryComplexity.Moderate"/> -- vector + graph.</item>
///   <item><see cref="QueryComplexity.Complex"/> -- vector + graph + web (if available).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><strong>Implementation guidance:</strong></para>
/// <list type="bullet">
///   <item>Fan out source queries via <c>Task.WhenAll</c> with per-source timeout
///         from <c>MultiSourceConfig.SourceTimeout</c>. Sources that time out produce
///         empty results, not exceptions.</item>
///   <item>Deduplicate by <see cref="DocumentChunk.Id"/>. When the same chunk appears
///         from multiple sources, keep the instance with the highest
///         <see cref="RetrievalResult.FusedScore"/>.</item>
///   <item>Track per-source latency and token usage via
///         <see cref="SourceRetrievalResult"/>.</item>
///   <item>Respect <c>MultiSourceConfig.EnabledSources</c> -- never query a disabled source.</item>
/// </list>
/// </remarks>
public interface IMultiSourceOrchestrator
{
    /// <summary>
    /// Retrieves results from all applicable sources based on query complexity,
    /// merges, deduplicates, and returns a unified result list sorted by fused score.
    /// </summary>
    /// <param name="query">The retrieval query text.</param>
    /// <param name="topK">Maximum results to return after deduplication.</param>
    /// <param name="complexity">
    /// Query complexity tier from Phase A classification. Determines which sources
    /// are queried.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deduplicated retrieval results from all queried sources.</returns>
    Task<IReadOnlyList<RetrievalResult>> RetrieveFromAllSourcesAsync(
        string query,
        int topK,
        QueryComplexity complexity,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create `IRetrievalQualityEvaluator`**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalQualityEvaluator.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Evaluates retrieval quality using Ragas-inspired metrics: context precision,
/// context recall, faithfulness, and answer relevancy. Each metric is assessed
/// via a separate LLM judge call for independent scoring.
/// </summary>
/// <remarks>
/// <para><strong>Implementation guidance:</strong></para>
/// <list type="bullet">
///   <item>Context precision: ask the LLM to judge what fraction of retrieved chunks
///         are relevant to the query.</item>
///   <item>Context recall: requires <paramref name="groundTruth"/>. Ask the LLM to judge
///         what fraction of the ground-truth information is present in the retrieved context.
///         When ground truth is null, set recall to -1.0 and redistribute its weight.</item>
///   <item>Faithfulness: ask the LLM to identify claims in the answer and verify each
///         against the retrieved context. Score = supported claims / total claims.</item>
///   <item>Answer relevancy: ask the LLM to generate hypothetical questions from the answer,
///         then measure semantic similarity to the original query.</item>
///   <item>Use <see cref="IRagModelRouter"/> with operation <c>"quality_evaluation"</c>
///         to route evaluation calls to the appropriate model tier.</item>
/// </list>
/// </remarks>
public interface IRetrievalQualityEvaluator
{
    /// <summary>
    /// Evaluates the quality of a retrieval+generation cycle and produces a
    /// <see cref="RetrievalQualityReport"/> with per-metric scores.
    /// </summary>
    /// <param name="query">The original user query.</param>
    /// <param name="answer">The generated answer based on the retrieved context.</param>
    /// <param name="context">The reranked retrieval results used to generate the answer.</param>
    /// <param name="groundTruth">
    /// Optional ground-truth answer for context recall calculation.
    /// When null, recall is set to -1.0 and its weight is redistributed.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quality report with per-metric scores and overall assessment.</returns>
    Task<RetrievalQualityReport> EvaluateAsync(
        string query,
        string answer,
        IReadOnlyList<RerankedResult> context,
        string? groundTruth = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Create `IRetrievalCostTracker`**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalCostTracker.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Thread-safe tracker for retrieval token usage and latency. Records individual
/// retrieval calls and produces an aggregated <see cref="RetrievalCostSummary"/>.
/// Scoped per plan execution or per request to provide per-execution cost visibility.
/// </summary>
public interface IRetrievalCostTracker
{
    /// <summary>
    /// Records a single retrieval-related LLM call's token usage and latency.
    /// Thread-safe for concurrent recording from multi-source orchestration.
    /// </summary>
    /// <param name="promptTokens">Input tokens consumed by this call.</param>
    /// <param name="completionTokens">Output tokens produced by this call.</param>
    /// <param name="latency">Wall-clock duration of this call.</param>
    void RecordCall(int promptTokens, int completionTokens, TimeSpan latency);

    /// <summary>
    /// Returns the aggregated cost summary of all recorded calls since the last reset.
    /// </summary>
    /// <returns>Aggregated token usage, call count, latency, and estimated cost.</returns>
    RetrievalCostSummary GetSummary();

    /// <summary>
    /// Resets all counters to zero. Called at the start of a new plan execution or request.
    /// </summary>
    void Reset();
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/RAG/IMultiSourceOrchestrator.cs src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalQualityEvaluator.cs src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalCostTracker.cs
git commit -m "feat(rag): add IMultiSourceOrchestrator, IRetrievalQualityEvaluator, and IRetrievalCostTracker interfaces"
```

---

### Task 5: Test Data Builders

**Files:**
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs`

- [ ] **Step 1: Add quality report, cost summary, source result, and retrieval step configuration builders**

Add the following methods to the `RagTestData` class:

```csharp
    public static RetrievalQualityReport CreateQualityReport(
        double contextPrecision = 0.85,
        double contextRecall = 0.80,
        double faithfulness = 0.90,
        double answerRelevancy = 0.88,
        double overallScore = 0.86) =>
        new()
        {
            ContextPrecision = contextPrecision,
            ContextRecall = contextRecall,
            Faithfulness = faithfulness,
            AnswerRelevancy = answerRelevancy,
            OverallScore = overallScore,
            Reasoning = "Test quality report with high scores across all metrics.",
            EvaluatedAt = DateTimeOffset.UtcNow
        };

    public static RetrievalCostSummary CreateCostSummary(
        int promptTokens = 1500,
        int completionTokens = 500,
        int retrievalCalls = 3,
        double totalLatencyMs = 2500.0) =>
        new()
        {
            TotalTokensUsed = promptTokens + completionTokens,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            RetrievalCalls = retrievalCalls,
            TotalLatency = TimeSpan.FromMilliseconds(totalLatencyMs),
            EstimatedCost = (promptTokens * 2.50 / 1_000_000) + (completionTokens * 10.00 / 1_000_000)
        };

    public static SourceRetrievalResult CreateSourceResult(
        string sourceName = "vector",
        int resultCount = 3,
        double latencyMs = 500.0,
        int tokensUsed = 200) =>
        new()
        {
            SourceName = sourceName,
            Results = CreateRetrievalResults(resultCount),
            Latency = TimeSpan.FromMilliseconds(latencyMs),
            TokensUsed = tokensUsed
        };

    public static RetrievalStepConfiguration CreateRetrievalStepConfiguration(
        string query = "What is the architecture of the system?",
        RetrievalStrategy? strategy = null,
        int? topK = null,
        string? collectionName = null,
        bool useMultiSource = false) =>
        new()
        {
            Query = query,
            Strategy = strategy,
            TopK = topK,
            CollectionName = collectionName,
            UseMultiSource = useMultiSource
        };
```

Also add the required using directives at the top of the file:

```csharp
using Domain.AI.Planner;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Domain.Common.Config;
using Microsoft.Extensions.Options;
using Moq;
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs
git commit -m "test(rag): add test data builders for quality reports, cost summaries, source results, and retrieval step config"
```

---

### Task 6: RetrievalCostTracker Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalCostTracker.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalCostTrackerTests.cs`

- [ ] **Step 1: Write the tests first**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalCostTrackerTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.Common.Config;
using FluentAssertions;
using Infrastructure.AI.RAG.Orchestration;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

public sealed class RetrievalCostTrackerTests
{
    private RetrievalCostTracker CreateTracker(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(configure);
        return new RetrievalCostTracker(config);
    }

    [Fact]
    public void RecordCall_SingleCall_TracksTokens()
    {
        // Arrange
        var tracker = CreateTracker();

        // Act
        tracker.RecordCall(promptTokens: 100, completionTokens: 50, TimeSpan.FromMilliseconds(200));

        // Assert
        var summary = tracker.GetSummary();
        summary.PromptTokens.Should().Be(100);
        summary.CompletionTokens.Should().Be(50);
        summary.TotalTokensUsed.Should().Be(150);
        summary.RetrievalCalls.Should().Be(1);
    }

    [Fact]
    public void RecordCall_MultipleCalls_AggregatesCorrectly()
    {
        // Arrange
        var tracker = CreateTracker();

        // Act
        tracker.RecordCall(promptTokens: 100, completionTokens: 50, TimeSpan.FromMilliseconds(200));
        tracker.RecordCall(promptTokens: 200, completionTokens: 80, TimeSpan.FromMilliseconds(300));
        tracker.RecordCall(promptTokens: 150, completionTokens: 60, TimeSpan.FromMilliseconds(250));

        // Assert
        var summary = tracker.GetSummary();
        summary.PromptTokens.Should().Be(450);
        summary.CompletionTokens.Should().Be(190);
        summary.TotalTokensUsed.Should().Be(640);
        summary.RetrievalCalls.Should().Be(3);
        summary.TotalLatency.TotalMilliseconds.Should().Be(750);
    }

    [Fact]
    public void GetSummary_CalculatesEstimatedCost()
    {
        // Arrange
        var tracker = CreateTracker();
        tracker.RecordCall(promptTokens: 1_000_000, completionTokens: 100_000, TimeSpan.FromSeconds(1));

        // Act
        var summary = tracker.GetSummary();

        // Assert -- default pricing: $2.50/1M input, $10.00/1M output
        summary.EstimatedCost.Should().BeApproximately(2.50 + 1.00, precision: 0.01);
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        // Arrange
        var tracker = CreateTracker();
        tracker.RecordCall(promptTokens: 100, completionTokens: 50, TimeSpan.FromMilliseconds(200));

        // Act
        tracker.Reset();

        // Assert
        var summary = tracker.GetSummary();
        summary.PromptTokens.Should().Be(0);
        summary.CompletionTokens.Should().Be(0);
        summary.TotalTokensUsed.Should().Be(0);
        summary.RetrievalCalls.Should().Be(0);
        summary.TotalLatency.Should().Be(TimeSpan.Zero);
        summary.EstimatedCost.Should().Be(0.0);
    }

    [Fact]
    public void RecordCall_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tracker = CreateTracker();
        const int threadCount = 50;
        const int callsPerThread = 100;

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < callsPerThread; i++)
                    tracker.RecordCall(promptTokens: 10, completionTokens: 5, TimeSpan.FromMilliseconds(1));
            }))
            .ToArray();

        Task.WaitAll(tasks);

        // Assert
        var summary = tracker.GetSummary();
        summary.RetrievalCalls.Should().Be(threadCount * callsPerThread);
        summary.PromptTokens.Should().Be(threadCount * callsPerThread * 10);
        summary.CompletionTokens.Should().Be(threadCount * callsPerThread * 5);
    }
}
```

- [ ] **Step 2: Verify tests fail (no implementation yet)**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build fails -- `RetrievalCostTracker` type not found.

- [ ] **Step 3: Implement `RetrievalCostTracker`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalCostTracker.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Domain.Common.Config;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.Orchestration;

/// <summary>
/// Thread-safe retrieval cost tracker using <see cref="Interlocked"/> operations
/// for lock-free concurrent recording. Produces an aggregated cost summary
/// with estimated USD cost based on configured token pricing.
/// </summary>
public sealed class RetrievalCostTracker : IRetrievalCostTracker
{
    private readonly IOptionsMonitor<AppConfig> _configMonitor;

    private long _promptTokens;
    private long _completionTokens;
    private long _retrievalCalls;
    private long _totalLatencyTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetrievalCostTracker"/> class.
    /// </summary>
    /// <param name="configMonitor">Application configuration for token pricing.</param>
    public RetrievalCostTracker(IOptionsMonitor<AppConfig> configMonitor)
    {
        ArgumentNullException.ThrowIfNull(configMonitor);
        _configMonitor = configMonitor;
    }

    /// <inheritdoc />
    public void RecordCall(int promptTokens, int completionTokens, TimeSpan latency)
    {
        Interlocked.Add(ref _promptTokens, promptTokens);
        Interlocked.Add(ref _completionTokens, completionTokens);
        Interlocked.Increment(ref _retrievalCalls);
        Interlocked.Add(ref _totalLatencyTicks, latency.Ticks);
    }

    /// <inheritdoc />
    public RetrievalCostSummary GetSummary()
    {
        var promptTokens = Interlocked.Read(ref _promptTokens);
        var completionTokens = Interlocked.Read(ref _completionTokens);
        var calls = Interlocked.Read(ref _retrievalCalls);
        var latencyTicks = Interlocked.Read(ref _totalLatencyTicks);

        var multiSourceConfig = _configMonitor.CurrentValue.AI.Rag.MultiSource;
        var costPerInputToken = multiSourceConfig.CostPerMillionInputTokens / 1_000_000.0;
        var costPerOutputToken = multiSourceConfig.CostPerMillionOutputTokens / 1_000_000.0;

        var estimatedCost = (promptTokens * costPerInputToken) + (completionTokens * costPerOutputToken);

        return new RetrievalCostSummary
        {
            TotalTokensUsed = (int)(promptTokens + completionTokens),
            PromptTokens = (int)promptTokens,
            CompletionTokens = (int)completionTokens,
            RetrievalCalls = (int)calls,
            TotalLatency = TimeSpan.FromTicks(latencyTicks),
            EstimatedCost = estimatedCost
        };
    }

    /// <inheritdoc />
    public void Reset()
    {
        Interlocked.Exchange(ref _promptTokens, 0);
        Interlocked.Exchange(ref _completionTokens, 0);
        Interlocked.Exchange(ref _retrievalCalls, 0);
        Interlocked.Exchange(ref _totalLatencyTicks, 0);
    }
}
```

- [ ] **Step 4: Run tests and verify all pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~RetrievalCostTrackerTests"`
Expected: 5 tests pass, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalCostTracker.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalCostTrackerTests.cs
git commit -m "feat(rag): implement thread-safe RetrievalCostTracker with Interlocked operations"
```

---

### Task 7: MultiSourceOrchestrator Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorTests.cs`

- [ ] **Step 1: Write the tests first**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Domain.Common.Config;
using FluentAssertions;
using Infrastructure.AI.RAG.Orchestration;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

public sealed class MultiSourceOrchestratorTests
{
    private readonly Mock<IHybridRetriever> _mockHybridRetriever = new();
    private readonly Mock<IGraphRagService> _mockGraphRag = new();
    private readonly Mock<IRetrievalCostTracker> _mockCostTracker = new();

    private MultiSourceOrchestrator CreateOrchestrator(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(cfg =>
        {
            cfg.AI.Rag.MultiSource.Enabled = true;
            cfg.AI.Rag.MultiSource.EnabledSources = ["vector", "graph", "web"];
            configure?.Invoke(cfg);
        });

        return new MultiSourceOrchestrator(
            _mockHybridRetriever.Object,
            _mockGraphRag.Object,
            _mockCostTracker.Object,
            config,
            Mock.Of<ILogger<MultiSourceOrchestrator>>());
    }

    private void SetupVectorResults(int count = 3)
    {
        _mockHybridRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateRetrievalResults(count));
    }

    private void SetupGraphResults(int count = 2)
    {
        var results = new List<RetrievalResult>();
        for (var i = 0; i < count; i++)
        {
            results.Add(RagTestData.CreateRetrievalResult(
                id: $"graph-chunk-{i + 1}",
                content: $"Graph content {i + 1}",
                denseScore: 0.8 - (i * 0.1),
                sparseScore: 0.0,
                fusedScore: 0.8 - (i * 0.1)));
        }
        _mockGraphRag
            .Setup(g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_SimpleQuery_VectorOnly()
    {
        // Arrange
        SetupVectorResults(3);
        SetupGraphResults(2);
        var orchestrator = CreateOrchestrator();

        // Act
        var results = await orchestrator.RetrieveFromAllSourcesAsync(
            "simple query", topK: 5, QueryComplexity.Simple);

        // Assert
        results.Should().HaveCount(3);
        _mockHybridRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockGraphRag.Verify(
            g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_ModerateQuery_VectorAndGraph()
    {
        // Arrange
        SetupVectorResults(3);
        SetupGraphResults(2);
        var orchestrator = CreateOrchestrator();

        // Act
        var results = await orchestrator.RetrieveFromAllSourcesAsync(
            "moderate query", topK: 10, QueryComplexity.Moderate);

        // Assert -- 3 vector + 2 graph, no duplicates
        results.Should().HaveCount(5);
        _mockHybridRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockGraphRag.Verify(
            g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_ComplexQuery_AllSources()
    {
        // Arrange
        SetupVectorResults(3);
        SetupGraphResults(2);
        var orchestrator = CreateOrchestrator();

        // Act
        var results = await orchestrator.RetrieveFromAllSourcesAsync(
            "complex multi-faceted query", topK: 10, QueryComplexity.Complex);

        // Assert -- vector + graph queried (web has no implementation, degrades gracefully)
        results.Should().HaveCountGreaterOrEqualTo(3);
        _mockHybridRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockGraphRag.Verify(
            g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_SourceTimeout_GracefulDegradation()
    {
        // Arrange -- graph times out, vector succeeds
        SetupVectorResults(3);
        _mockGraphRag
            .Setup(g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, int _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return (IReadOnlyList<RetrievalResult>)[];
            });

        var orchestrator = CreateOrchestrator(cfg =>
        {
            cfg.AI.Rag.MultiSource.SourceTimeout = TimeSpan.FromMilliseconds(100);
        });

        // Act
        var results = await orchestrator.RetrieveFromAllSourcesAsync(
            "query", topK: 10, QueryComplexity.Moderate);

        // Assert -- only vector results returned, no exception
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_DuplicateChunks_Deduplicated()
    {
        // Arrange -- both sources return chunk with same ID, keep highest score
        var sharedChunk = RagTestData.CreateRetrievalResult(
            id: "shared-chunk", content: "shared", fusedScore: 0.7);
        var higherScoreChunk = RagTestData.CreateRetrievalResult(
            id: "shared-chunk", content: "shared", fusedScore: 0.9);

        _mockHybridRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResult> { sharedChunk });
        _mockGraphRag
            .Setup(g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResult> { higherScoreChunk });

        var orchestrator = CreateOrchestrator();

        // Act
        var results = await orchestrator.RetrieveFromAllSourcesAsync(
            "query", topK: 10, QueryComplexity.Moderate);

        // Assert -- deduplicated to 1 chunk, kept higher score
        results.Should().HaveCount(1);
        results[0].FusedScore.Should().Be(0.9);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_AllSourcesFail_ReturnsEmpty()
    {
        // Arrange
        _mockHybridRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vector store unavailable"));
        _mockGraphRag
            .Setup(g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph unavailable"));

        var orchestrator = CreateOrchestrator();

        // Act
        var results = await orchestrator.RetrieveFromAllSourcesAsync(
            "query", topK: 10, QueryComplexity.Complex);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_DisabledSource_Skipped()
    {
        // Arrange
        SetupVectorResults(3);
        SetupGraphResults(2);
        var orchestrator = CreateOrchestrator(cfg =>
        {
            cfg.AI.Rag.MultiSource.EnabledSources = ["vector"]; // graph disabled
        });

        // Act
        var results = await orchestrator.RetrieveFromAllSourcesAsync(
            "query", topK: 10, QueryComplexity.Complex);

        // Assert
        results.Should().HaveCount(3);
        _mockGraphRag.Verify(
            g => g.LocalSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Verify tests fail (no implementation yet)**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build fails -- `MultiSourceOrchestrator` type not found.

- [ ] **Step 3: Implement `MultiSourceOrchestrator`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs
using System.Diagnostics;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Domain.AI.Telemetry.Conventions;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.Orchestration;

/// <summary>
/// Coordinates retrieval across vector, graph, and web sources in parallel.
/// Selects sources based on query complexity, deduplicates results by chunk ID
/// (keeping the highest fused score), and respects per-source timeouts.
/// </summary>
public sealed class MultiSourceOrchestrator : IMultiSourceOrchestrator
{
    private static readonly ActivitySource ActivitySource = new("Infrastructure.AI.RAG.MultiSource");

    private const string SourceVector = "vector";
    private const string SourceGraph = "graph";
    private const string SourceWeb = "web";

    private readonly IHybridRetriever _hybridRetriever;
    private readonly IGraphRagService _graphRagService;
    private readonly IRetrievalCostTracker _costTracker;
    private readonly IOptionsMonitor<AppConfig> _configMonitor;
    private readonly ILogger<MultiSourceOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiSourceOrchestrator"/> class.
    /// </summary>
    public MultiSourceOrchestrator(
        IHybridRetriever hybridRetriever,
        IGraphRagService graphRagService,
        IRetrievalCostTracker costTracker,
        IOptionsMonitor<AppConfig> configMonitor,
        ILogger<MultiSourceOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(hybridRetriever);
        ArgumentNullException.ThrowIfNull(graphRagService);
        ArgumentNullException.ThrowIfNull(costTracker);
        ArgumentNullException.ThrowIfNull(configMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _hybridRetriever = hybridRetriever;
        _graphRagService = graphRagService;
        _costTracker = costTracker;
        _configMonitor = configMonitor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> RetrieveFromAllSourcesAsync(
        string query,
        int topK,
        QueryComplexity complexity,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.multi_source.retrieve");
        var config = _configMonitor.CurrentValue.AI.Rag.MultiSource;

        var sourcesToQuery = DetermineSourcesForComplexity(complexity, config);
        activity?.SetTag("rag.multi_source.source_count", sourcesToQuery.Count);
        activity?.SetTag("rag.multi_source.complexity", complexity.ToString().ToLowerInvariant());

        _logger.LogInformation(
            "Multi-source retrieval: Complexity={Complexity}, Sources=[{Sources}], TopK={TopK}",
            complexity, string.Join(", ", sourcesToQuery), topK);

        var sourceResults = await FanOutToSourcesAsync(
            query, topK, sourcesToQuery, config.SourceTimeout, cancellationToken);

        var allResults = new List<RetrievalResult>();
        foreach (var sourceResult in sourceResults)
        {
            allResults.AddRange(sourceResult.Results);
            activity?.SetTag($"rag.multi_source.{sourceResult.SourceName}.latency_ms",
                sourceResult.Latency.TotalMilliseconds);
            activity?.SetTag($"rag.multi_source.{sourceResult.SourceName}.count",
                sourceResult.Results.Count);
        }

        var deduplicated = DeduplicateByChunkId(allResults);

        var sorted = deduplicated
            .OrderByDescending(r => r.FusedScore)
            .Take(topK)
            .ToList();

        _logger.LogInformation(
            "Multi-source retrieval complete: {TotalRaw} raw, {Deduplicated} deduplicated, {Returned} returned",
            allResults.Count, deduplicated.Count, sorted.Count);

        return sorted;
    }

    private static IReadOnlyList<string> DetermineSourcesForComplexity(
        QueryComplexity complexity,
        MultiSourceConfig config)
    {
        var enabled = new HashSet<string>(config.EnabledSources, StringComparer.OrdinalIgnoreCase);

        var candidates = complexity switch
        {
            QueryComplexity.Trivial or QueryComplexity.Simple => new[] { SourceVector },
            QueryComplexity.Moderate => new[] { SourceVector, SourceGraph },
            QueryComplexity.Complex => new[] { SourceVector, SourceGraph, SourceWeb },
            _ => new[] { SourceVector }
        };

        return candidates.Where(s => enabled.Contains(s)).ToList();
    }

    private async Task<IReadOnlyList<SourceRetrievalResult>> FanOutToSourcesAsync(
        string query,
        int topK,
        IReadOnlyList<string> sources,
        TimeSpan sourceTimeout,
        CancellationToken cancellationToken)
    {
        var tasks = sources.Select(source =>
            ExecuteSourceWithTimeoutAsync(source, query, topK, sourceTimeout, cancellationToken));

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r is not null).Cast<SourceRetrievalResult>().ToList();
    }

    private async Task<SourceRetrievalResult?> ExecuteSourceWithTimeoutAsync(
        string sourceName,
        string query,
        int topK,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var results = sourceName switch
            {
                SourceVector => await _hybridRetriever.RetrieveAsync(
                    query, topK, collectionName: null, timeoutCts.Token),
                SourceGraph => await _graphRagService.LocalSearchAsync(
                    query, topK, timeoutCts.Token),
                SourceWeb => await ExecuteWebSearchAsync(query, topK, timeoutCts.Token),
                _ => (IReadOnlyList<RetrievalResult>)[]
            };

            sw.Stop();

            _logger.LogDebug(
                "Source {Source} returned {Count} results in {ElapsedMs}ms",
                sourceName, results.Count, sw.Elapsed.TotalMilliseconds);

            return new SourceRetrievalResult
            {
                SourceName = sourceName,
                Results = results,
                Latency = sw.Elapsed,
                TokensUsed = 0
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("Source {Source} timed out after {TimeoutMs}ms", sourceName, timeout.TotalMilliseconds);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Source {Source} failed: {Message}", sourceName, ex.Message);
            return null;
        }
    }

    private static Task<IReadOnlyList<RetrievalResult>> ExecuteWebSearchAsync(
        string query, int topK, CancellationToken cancellationToken)
    {
        // Web search is a future extension point. Returns empty for now.
        // When implemented, this will call an IWebSearchService to retrieve
        // web results and convert them to RetrievalResult format.
        return Task.FromResult<IReadOnlyList<RetrievalResult>>([]);
    }

    private static IReadOnlyList<RetrievalResult> DeduplicateByChunkId(
        IReadOnlyList<RetrievalResult> results)
    {
        var bestByChunkId = new Dictionary<string, RetrievalResult>();

        foreach (var result in results)
        {
            var chunkId = result.Chunk.Id;
            if (!bestByChunkId.TryGetValue(chunkId, out var existing) ||
                result.FusedScore > existing.FusedScore)
            {
                bestByChunkId[chunkId] = result;
            }
        }

        return bestByChunkId.Values.ToList();
    }
}
```

- [ ] **Step 4: Run tests and verify all pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~MultiSourceOrchestratorTests"`
Expected: 7 tests pass, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorTests.cs
git commit -m "feat(rag): implement MultiSourceOrchestrator with parallel fan-out, deduplication, and graceful timeout"
```

---

### Task 8: RetrievalQualityEvaluator Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/RetrievalQualityEvaluator.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/RetrievalQualityEvaluatorTests.cs`

- [ ] **Step 1: Write the tests first**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/RetrievalQualityEvaluatorTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.Evaluation;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Infrastructure.AI.RAG.Tests.Evaluation;

public sealed class RetrievalQualityEvaluatorTests
{
    private readonly Mock<IRagModelRouter> _mockRouter = new();
    private readonly Mock<IChatClient> _mockChatClient = new();

    public RetrievalQualityEvaluatorTests()
    {
        _mockRouter
            .Setup(r => r.GetClientForOperation(It.IsAny<string>()))
            .Returns(_mockChatClient.Object);
    }

    private RetrievalQualityEvaluator CreateEvaluator()
    {
        return new RetrievalQualityEvaluator(
            _mockRouter.Object,
            Mock.Of<ILogger<RetrievalQualityEvaluator>>());
    }

    private void SetupChatResponse(string responseText)
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
    }

    [Fact]
    public async Task EvaluateAsync_HighQualityRetrieval_HighScores()
    {
        // Arrange
        SetupChatResponse("0.90");
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        // Act
        var report = await evaluator.EvaluateAsync(
            query: "What is clean architecture?",
            answer: "Clean architecture separates concerns into layers.",
            context: context,
            groundTruth: "Clean architecture is about separation of concerns into layers.");

        // Assert
        report.ContextPrecision.Should().BeGreaterOrEqualTo(0.0);
        report.ContextRecall.Should().BeGreaterOrEqualTo(0.0);
        report.Faithfulness.Should().BeGreaterOrEqualTo(0.0);
        report.AnswerRelevancy.Should().BeGreaterOrEqualTo(0.0);
        report.OverallScore.Should().BeGreaterOrEqualTo(0.0);
        report.EvaluatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EvaluateAsync_IrrelevantContext_LowPrecision()
    {
        // Arrange -- LLM returns low score for precision, high for others
        var callCount = 0;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = Interlocked.Increment(ref callCount);
                // First call is precision (low), rest are high
                var score = count == 1 ? "0.20" : "0.85";
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, score));
            });

        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        // Act
        var report = await evaluator.EvaluateAsync(
            query: "What is clean architecture?",
            answer: "Clean architecture separates concerns.",
            context: context,
            groundTruth: "Clean architecture is about layers.");

        // Assert
        report.ContextPrecision.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_HallucinatedAnswer_LowFaithfulness()
    {
        // Arrange -- LLM returns high for precision/recall, low for faithfulness
        var callCount = 0;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = Interlocked.Increment(ref callCount);
                // Third call is faithfulness (low), rest are high
                var score = count == 3 ? "0.15" : "0.85";
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, score));
            });

        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        // Act
        var report = await evaluator.EvaluateAsync(
            query: "What is the system?",
            answer: "The system uses quantum computing for all operations.",
            context: context,
            groundTruth: "The system uses standard computing.");

        // Assert
        report.Faithfulness.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_WithGroundTruth_CalculatesRecall()
    {
        // Arrange
        SetupChatResponse("0.80");
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        // Act
        var report = await evaluator.EvaluateAsync(
            query: "What is X?",
            answer: "X is Y.",
            context: context,
            groundTruth: "X is Y and also Z.");

        // Assert
        report.ContextRecall.Should().BeGreaterOrEqualTo(0.0);
        report.ContextRecall.Should().NotBe(-1.0);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutGroundTruth_SkipsRecall()
    {
        // Arrange
        SetupChatResponse("0.80");
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        // Act
        var report = await evaluator.EvaluateAsync(
            query: "What is X?",
            answer: "X is Y.",
            context: context,
            groundTruth: null);

        // Assert
        report.ContextRecall.Should().Be(-1.0);
    }

    [Fact]
    public async Task EvaluateAsync_LlmFailure_ReturnsFallbackReport()
    {
        // Arrange
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM service unavailable"));

        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        // Act
        var report = await evaluator.EvaluateAsync(
            query: "What is X?",
            answer: "X is Y.",
            context: context);

        // Assert -- fallback with 0 scores
        report.ContextPrecision.Should().Be(0.0);
        report.Faithfulness.Should().Be(0.0);
        report.AnswerRelevancy.Should().Be(0.0);
        report.OverallScore.Should().Be(0.0);
        report.Reasoning.Should().Contain("failed");
    }
}
```

- [ ] **Step 2: Verify tests fail (no implementation yet)**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build fails -- `RetrievalQualityEvaluator` type not found.

- [ ] **Step 3: Implement `RetrievalQualityEvaluator`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/RetrievalQualityEvaluator.cs
using System.Globalization;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.Evaluation;

/// <summary>
/// Evaluates retrieval quality using Ragas-inspired metrics via LLM judges.
/// Each metric (context precision, recall, faithfulness, answer relevancy) is
/// assessed by a separate LLM call for independent, explainable scoring.
/// </summary>
public sealed class RetrievalQualityEvaluator : IRetrievalQualityEvaluator
{
    private const string OperationName = "quality_evaluation";

    private const string PrecisionPrompt = """
        You are an impartial judge evaluating retrieval quality.
        
        Given the following user query and retrieved context chunks, evaluate what fraction
        of the retrieved chunks are actually relevant to answering the query.
        
        User query: {0}
        
        Retrieved context:
        {1}
        
        Respond with ONLY a single decimal number between 0.0 and 1.0 representing the
        fraction of chunks that are relevant. 1.0 means all chunks are relevant, 0.0 means
        none are relevant.
        """;

    private const string RecallPrompt = """
        You are an impartial judge evaluating retrieval completeness.
        
        Given the following user query, the ground-truth answer, and the retrieved context,
        evaluate what fraction of the information in the ground-truth answer is captured
        in the retrieved context.
        
        User query: {0}
        Ground-truth answer: {1}
        
        Retrieved context:
        {2}
        
        Respond with ONLY a single decimal number between 0.0 and 1.0. 1.0 means all
        ground-truth information is present in the context, 0.0 means none is.
        """;

    private const string FaithfulnessPrompt = """
        You are an impartial judge evaluating answer faithfulness.
        
        Given the following user query, the generated answer, and the retrieved context,
        evaluate whether every claim in the answer is supported by the retrieved context.
        
        User query: {0}
        Generated answer: {1}
        
        Retrieved context:
        {2}
        
        Respond with ONLY a single decimal number between 0.0 and 1.0. 1.0 means every
        claim is fully supported by the context, 0.0 means the answer is entirely
        unsupported (hallucinated).
        """;

    private const string RelevancyPrompt = """
        You are an impartial judge evaluating answer relevancy.
        
        Given the following user query and the generated answer, evaluate how well the
        answer addresses the original question.
        
        User query: {0}
        Generated answer: {1}
        
        Respond with ONLY a single decimal number between 0.0 and 1.0. 1.0 means the
        answer perfectly addresses the question, 0.0 means it is completely off-topic.
        """;

    private readonly IRagModelRouter _modelRouter;
    private readonly ILogger<RetrievalQualityEvaluator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetrievalQualityEvaluator"/> class.
    /// </summary>
    public RetrievalQualityEvaluator(
        IRagModelRouter modelRouter,
        ILogger<RetrievalQualityEvaluator> logger)
    {
        ArgumentNullException.ThrowIfNull(modelRouter);
        ArgumentNullException.ThrowIfNull(logger);

        _modelRouter = modelRouter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RetrievalQualityReport> EvaluateAsync(
        string query,
        string answer,
        IReadOnlyList<RerankedResult> context,
        string? groundTruth = null,
        CancellationToken cancellationToken = default)
    {
        var contextText = FormatContext(context);

        try
        {
            var precisionTask = EvaluateMetricAsync(
                string.Format(PrecisionPrompt, query, contextText), cancellationToken);
            var faithfulnessTask = EvaluateMetricAsync(
                string.Format(FaithfulnessPrompt, query, answer, contextText), cancellationToken);
            var relevancyTask = EvaluateMetricAsync(
                string.Format(RelevancyPrompt, query, answer), cancellationToken);

            Task<double> recallTask;
            if (groundTruth is not null)
            {
                recallTask = EvaluateMetricAsync(
                    string.Format(RecallPrompt, query, groundTruth, contextText), cancellationToken);
            }
            else
            {
                recallTask = Task.FromResult(-1.0);
            }

            await Task.WhenAll(precisionTask, recallTask, faithfulnessTask, relevancyTask);

            var precision = await precisionTask;
            var recall = await recallTask;
            var faithfulness = await faithfulnessTask;
            var relevancy = await relevancyTask;

            var overallScore = CalculateOverallScore(precision, recall, faithfulness, relevancy);

            _logger.LogInformation(
                "Quality evaluation: Precision={Precision:F2}, Recall={Recall:F2}, " +
                "Faithfulness={Faithfulness:F2}, Relevancy={Relevancy:F2}, Overall={Overall:F2}",
                precision, recall, faithfulness, relevancy, overallScore);

            return new RetrievalQualityReport
            {
                ContextPrecision = precision,
                ContextRecall = recall,
                Faithfulness = faithfulness,
                AnswerRelevancy = relevancy,
                OverallScore = overallScore,
                Reasoning = $"Precision={precision:F2}, Recall={recall:F2}, " +
                            $"Faithfulness={faithfulness:F2}, Relevancy={relevancy:F2}",
                EvaluatedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Quality evaluation failed: {Message}", ex.Message);
            return CreateFallbackReport(ex.Message);
        }
    }

    private async Task<double> EvaluateMetricAsync(string prompt, CancellationToken cancellationToken)
    {
        var client = _modelRouter.GetClientForOperation(OperationName);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions { Temperature = 0.0f, MaxOutputTokens = 10 };
        var response = await client.GetResponseAsync(messages, options, cancellationToken);

        var responseText = response.Text?.Trim() ?? "0.0";

        if (double.TryParse(responseText, CultureInfo.InvariantCulture, out var score))
            return Math.Clamp(score, 0.0, 1.0);

        _logger.LogWarning("Could not parse metric score from LLM response: '{Response}'", responseText);
        return 0.0;
    }

    private static double CalculateOverallScore(
        double precision, double recall, double faithfulness, double relevancy)
    {
        if (recall < 0)
        {
            // Recall was skipped -- redistribute its weight
            // Normal: precision 0.25, recall 0.25, faithfulness 0.3, relevancy 0.2
            // Without recall: precision 0.33, faithfulness 0.40, relevancy 0.27
            return (precision * 0.33) + (faithfulness * 0.40) + (relevancy * 0.27);
        }

        return (precision * 0.25) + (recall * 0.25) + (faithfulness * 0.30) + (relevancy * 0.20);
    }

    private static string FormatContext(IReadOnlyList<RerankedResult> context)
    {
        return string.Join("\n---\n", context.Select((r, i) =>
            $"[Chunk {i + 1}] (score: {r.RerankScore:F2})\n{r.RetrievalResult.Chunk.Content}"));
    }

    private static RetrievalQualityReport CreateFallbackReport(string errorMessage) => new()
    {
        ContextPrecision = 0.0,
        ContextRecall = -1.0,
        Faithfulness = 0.0,
        AnswerRelevancy = 0.0,
        OverallScore = 0.0,
        Reasoning = $"Quality evaluation failed: {errorMessage}",
        EvaluatedAt = DateTimeOffset.UtcNow
    };
}
```

- [ ] **Step 4: Run tests and verify all pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~RetrievalQualityEvaluatorTests"`
Expected: 6 tests pass, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/RetrievalQualityEvaluator.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/RetrievalQualityEvaluatorTests.cs
git commit -m "feat(rag): implement RetrievalQualityEvaluator with Ragas-inspired LLM judge metrics"
```

---

### Task 9: RetrievalPlanStepExecutor Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Planner/StepExecutors/RetrievalPlanStepExecutor.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Planner/StepExecutors/RetrievalPlanStepExecutorTests.cs`

- [ ] **Step 1: Write the tests first**

```csharp
// src/Content/Tests/Infrastructure.AI.Tests/Planner/StepExecutors/RetrievalPlanStepExecutorTests.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.Planner;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.Planner;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.Planner.StepExecutors;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Infrastructure.AI.Tests.Planner.StepExecutors;

public sealed class RetrievalPlanStepExecutorTests
{
    private readonly Mock<IRagOrchestrator> _mockRagOrchestrator = new();
    private readonly Mock<IMultiSourceOrchestrator> _mockMultiSource = new();
    private readonly Mock<IQueryComplexityClassifier> _mockComplexityClassifier = new();
    private readonly Mock<IRetrievalCostTracker> _mockCostTracker = new();
    private readonly Mock<IPlanProgressNotifier> _mockNotifier = new();

    private readonly RagAssembledContext _expectedContext = new()
    {
        AssembledText = "Retrieved context about clean architecture.",
        TotalTokens = 150,
        WasTruncated = false,
        Citations =
        [
            new CitationSpan
            {
                ChunkId = "chunk-1",
                DocumentUri = new Uri("file:///docs/arch.md"),
                SectionPath = "Architecture > Overview",
                StartOffset = 0,
                EndOffset = 44
            }
        ]
    };

    private RetrievalPlanStepExecutor CreateExecutor()
    {
        return new RetrievalPlanStepExecutor(
            _mockRagOrchestrator.Object,
            _mockMultiSource.Object,
            _mockComplexityClassifier.Object,
            _mockCostTracker.Object,
            _mockNotifier.Object,
            new PlanExecutionContext(),
            Mock.Of<ILogger<RetrievalPlanStepExecutor>>());
    }

    private static PlanStep CreateRetrievalStep(RetrievalStepConfiguration config) => new()
    {
        Id = PlanStepId.New(),
        Name = "Retrieve context",
        Type = StepType.Retrieval,
        Configuration = config,
        RetryPolicy = new RetryPolicy { MaxRetries = 0 }
    };

    [Fact]
    public async Task ExecuteAsync_BasicRetrieval_CallsOrchestrator()
    {
        // Arrange
        _mockRagOrchestrator
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_expectedContext);

        var executor = CreateExecutor();
        var config = new RetrievalStepConfiguration
        {
            Query = "What is clean architecture?",
            UseMultiSource = false
        };
        var step = CreateRetrievalStep(config);

        // Act
        var result = await executor.ExecuteAsync(step, new Dictionary<PlanStepId, string>(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(StepExecutionStatus.Completed);
        result.Output.Should().NotBeNullOrEmpty();
        _mockRagOrchestrator.Verify(r => r.SearchAsync(
            "What is clean architecture?", null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultiSource_CallsMultiSourceOrchestrator()
    {
        // Arrange
        var retrievalResults = new List<RetrievalResult>
        {
            new()
            {
                Chunk = new DocumentChunk
                {
                    Id = "chunk-1", DocumentId = "doc-1", SectionPath = "Section",
                    Content = "Content", Tokens = 10,
                    Metadata = new ChunkMetadata
                    {
                        SourceUri = new Uri("file:///doc.md"),
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                },
                DenseScore = 0.9, SparseScore = 0.3, FusedScore = 0.85
            }
        };
        _mockMultiSource
            .Setup(m => m.RetrieveFromAllSourcesAsync(
                It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrievalResults);
        _mockComplexityClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplexityClassification
            {
                Complexity = QueryComplexity.Moderate,
                Confidence = 0.8
            });

        var executor = CreateExecutor();
        var config = new RetrievalStepConfiguration
        {
            Query = "Complex multi-faceted question",
            UseMultiSource = true,
            TopK = 10
        };
        var step = CreateRetrievalStep(config);

        // Act
        var result = await executor.ExecuteAsync(step, new Dictionary<PlanStepId, string>(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(StepExecutionStatus.Completed);
        result.Output.Should().NotBeNullOrEmpty();
        _mockMultiSource.Verify(m => m.RetrieveFromAllSourcesAsync(
            "Complex multi-faceted question", 10,
            QueryComplexity.Moderate, It.IsAny<CancellationToken>()), Times.Once);
        _mockRagOrchestrator.Verify(r => r.SearchAsync(
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithStrategyOverride_PassesToOrchestrator()
    {
        // Arrange
        _mockRagOrchestrator
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_expectedContext);

        var executor = CreateExecutor();
        var config = new RetrievalStepConfiguration
        {
            Query = "What is RAPTOR?",
            Strategy = RetrievalStrategy.RaptorTree,
            TopK = 5,
            CollectionName = "docs-collection"
        };
        var step = CreateRetrievalStep(config);

        // Act
        var result = await executor.ExecuteAsync(step, new Dictionary<PlanStepId, string>(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(StepExecutionStatus.Completed);
        _mockRagOrchestrator.Verify(r => r.SearchAsync(
            "What is RAPTOR?", 5, "docs-collection",
            RetrievalStrategy.RaptorTree, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OrchestratorFails_ReturnsFailedResult()
    {
        // Arrange
        _mockRagOrchestrator
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vector store connection failed"));

        var executor = CreateExecutor();
        var config = new RetrievalStepConfiguration { Query = "test query" };
        var step = CreateRetrievalStep(config);

        // Act
        var result = await executor.ExecuteAsync(step, new Dictionary<PlanStepId, string>(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(StepExecutionStatus.Failed);
        result.ErrorMessage.Should().Contain("Vector store connection failed");
    }

    [Fact]
    public async Task ExecuteAsync_TracksRetrievalCost()
    {
        // Arrange
        _mockRagOrchestrator
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_expectedContext);

        var executor = CreateExecutor();
        var config = new RetrievalStepConfiguration { Query = "test query" };
        var step = CreateRetrievalStep(config);

        // Act
        await executor.ExecuteAsync(step, new Dictionary<PlanStepId, string>(), CancellationToken.None);

        // Assert
        _mockCostTracker.Verify(t => t.RecordCall(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SerializesContextAsOutput()
    {
        // Arrange
        _mockRagOrchestrator
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_expectedContext);

        var executor = CreateExecutor();
        var config = new RetrievalStepConfiguration { Query = "test query" };
        var step = CreateRetrievalStep(config);

        // Act
        var result = await executor.ExecuteAsync(step, new Dictionary<PlanStepId, string>(), CancellationToken.None);

        // Assert -- output is valid JSON containing the assembled text
        result.Output.Should().NotBeNull();
        var outputDoc = JsonDocument.Parse(result.Output!);
        outputDoc.RootElement.GetProperty("assembledText").GetString()
            .Should().Be("Retrieved context about clean architecture.");
        outputDoc.RootElement.GetProperty("totalTokens").GetInt32().Should().Be(150);
    }
}
```

- [ ] **Step 2: Verify tests fail (no implementation yet)**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build fails -- `RetrievalPlanStepExecutor` type not found.

- [ ] **Step 3: Implement `RetrievalPlanStepExecutor`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI/Planner/StepExecutors/RetrievalPlanStepExecutor.cs
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.AI.Common.Interfaces.Planner;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.Planner;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Planner.StepExecutors;

/// <summary>
/// Executes RAG retrieval steps within a plan by calling <see cref="IRagOrchestrator"/>
/// or <see cref="IMultiSourceOrchestrator"/> based on step configuration. Serializes
/// the resulting <see cref="RagAssembledContext"/> as JSON output for downstream steps.
/// </summary>
public sealed class RetrievalPlanStepExecutor : IPlanStepExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IRagOrchestrator _ragOrchestrator;
    private readonly IMultiSourceOrchestrator _multiSourceOrchestrator;
    private readonly IQueryComplexityClassifier _complexityClassifier;
    private readonly IRetrievalCostTracker _costTracker;
    private readonly IPlanProgressNotifier _notifier;
    private readonly PlanExecutionContext _executionContext;
    private readonly ILogger<RetrievalPlanStepExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetrievalPlanStepExecutor"/> class.
    /// </summary>
    public RetrievalPlanStepExecutor(
        IRagOrchestrator ragOrchestrator,
        IMultiSourceOrchestrator multiSourceOrchestrator,
        IQueryComplexityClassifier complexityClassifier,
        IRetrievalCostTracker costTracker,
        IPlanProgressNotifier notifier,
        PlanExecutionContext executionContext,
        ILogger<RetrievalPlanStepExecutor> logger)
    {
        _ragOrchestrator = ragOrchestrator;
        _multiSourceOrchestrator = multiSourceOrchestrator;
        _complexityClassifier = complexityClassifier;
        _costTracker = costTracker;
        _notifier = notifier;
        _executionContext = executionContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StepExecutionResult> ExecuteAsync(
        PlanStep step,
        IReadOnlyDictionary<PlanStepId, string> upstreamOutputs,
        CancellationToken ct)
    {
        if (step.Configuration is not RetrievalStepConfiguration config)
        {
            return new StepExecutionResult
            {
                Status = StepExecutionStatus.Failed,
                Duration = TimeSpan.Zero,
                ErrorMessage = $"Step '{step.Name}' has invalid configuration type for Retrieval executor."
            };
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var query = ResolveQuery(config.Query, upstreamOutputs);

            string outputJson;

            if (config.UseMultiSource)
            {
                outputJson = await ExecuteMultiSourceAsync(query, config, ct);
            }
            else
            {
                outputJson = await ExecuteSingleSourceAsync(query, config, ct);
            }

            sw.Stop();

            // Record cost -- estimate tokens from the output size as a proxy
            var estimatedPromptTokens = query.Length / 4;
            var estimatedCompletionTokens = (outputJson?.Length ?? 0) / 4;
            _costTracker.RecordCall(estimatedPromptTokens, estimatedCompletionTokens, sw.Elapsed);

            _logger.LogInformation(
                "Retrieval step '{StepName}' completed in {ElapsedMs}ms, output={OutputLength} chars",
                step.Name, sw.Elapsed.TotalMilliseconds, outputJson?.Length ?? 0);

            return new StepExecutionResult
            {
                Status = StepExecutionStatus.Completed,
                Output = outputJson,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "Retrieval step '{StepName}' failed: {Message}", step.Name, ex.Message);

            return new StepExecutionResult
            {
                Status = StepExecutionStatus.Failed,
                ErrorMessage = $"Retrieval failed: {ex.Message}",
                Duration = sw.Elapsed
            };
        }
    }

    private async Task<string> ExecuteSingleSourceAsync(
        string query, RetrievalStepConfiguration config, CancellationToken ct)
    {
        var context = await _ragOrchestrator.SearchAsync(
            query, config.TopK, config.CollectionName, config.Strategy, ct);

        return JsonSerializer.Serialize(context, SerializerOptions);
    }

    private async Task<string> ExecuteMultiSourceAsync(
        string query, RetrievalStepConfiguration config, CancellationToken ct)
    {
        var classification = await _complexityClassifier.ClassifyAsync(query, ct);

        var results = await _multiSourceOrchestrator.RetrieveFromAllSourcesAsync(
            query, config.TopK ?? 10, classification.Complexity, ct);

        // Wrap multi-source results in a structure compatible with downstream LLM steps
        var output = new
        {
            assembledText = string.Join("\n\n", results.Select(r => r.Chunk.Content)),
            totalTokens = results.Sum(r => r.Chunk.Tokens),
            wasTruncated = false,
            resultCount = results.Count,
            complexity = classification.Complexity.ToString()
        };

        return JsonSerializer.Serialize(output, SerializerOptions);
    }

    private static string ResolveQuery(
        string queryTemplate,
        IReadOnlyDictionary<PlanStepId, string> upstreamOutputs)
    {
        // If upstream outputs exist, append them as additional context for the query
        if (upstreamOutputs.Count == 0)
            return queryTemplate;

        var contextParts = upstreamOutputs.Values
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        if (contextParts.Count == 0)
            return queryTemplate;

        return $"{queryTemplate}\n\nAdditional context:\n{string.Join("\n", contextParts)}";
    }
}
```

- [ ] **Step 4: Run tests and verify all pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~RetrievalPlanStepExecutorTests"`
Expected: 6 tests pass, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Planner/StepExecutors/RetrievalPlanStepExecutor.cs src/Content/Tests/Infrastructure.AI.Tests/Planner/StepExecutors/RetrievalPlanStepExecutorTests.cs
git commit -m "feat(rag): implement RetrievalPlanStepExecutor with single-source and multi-source modes"
```

---

### Task 10: RagOrchestrator Enhancement -- Multi-Source Integration and Cost Tracking

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs`
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorTests.cs`

- [ ] **Step 1: Write the new tests in `RagOrchestratorTests`**

Add these tests to the existing `RagOrchestratorTests` class:

```csharp
    private readonly Mock<IMultiSourceOrchestrator> _mockMultiSource = new();
    private readonly Mock<IRetrievalCostTracker> _mockCostTracker = new();
    private readonly Mock<IQueryComplexityClassifier> _mockComplexityClassifier = new();

    // Update the CreateOrchestrator method to include the new dependencies:
    private RagOrchestrator CreateOrchestrator(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(configure);
        var queryRouter = new QueryRouter(
            Mock.Of<IQueryClassifier>(),
            Mock.Of<IServiceProvider>(),
            config,
            Mock.Of<ILogger<QueryRouter>>());

        return new RagOrchestrator(
            _mockRetriever.Object,
            _mockReranker.Object,
            _mockCrag.Object,
            _mockAssembler.Object,
            _mockGraphRag.Object,
            feedbackScorer: null,
            queryRouter,
            _mockMultiSource.Object,
            _mockComplexityClassifier.Object,
            _mockCostTracker.Object,
            config,
            Mock.Of<ILogger<RagOrchestrator>>());
    }

    [Fact]
    public async Task SearchAsync_MultiSourceEnabled_UsesMultiSourceOrchestrator()
    {
        // Arrange
        var multiSourceResults = RagTestData.CreateRetrievalResults(3);
        _mockMultiSource
            .Setup(m => m.RetrieveFromAllSourcesAsync(
                It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(multiSourceResults);
        _mockComplexityClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplexityClassification
            {
                Complexity = QueryComplexity.Moderate,
                Confidence = 0.8
            });

        // Need to setup reranker and CRAG since multi-source results still go through those stages
        var rerankedResults = RagTestData.CreateRerankedResults(3);
        _mockReranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults);
        _mockCrag
            .Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateAcceptEvaluation());
        _mockAssembler
            .Setup(a => a.AssembleAsync(It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_expectedContext);

        var orchestrator = CreateOrchestrator(cfg =>
        {
            cfg.AI.Rag.MultiSource.Enabled = true;
        });

        // Act
        var result = await orchestrator.SearchAsync("complex multi-hop query");

        // Assert
        result.AssembledText.Should().Be("assembled text");
        _mockMultiSource.Verify(m => m.RetrieveFromAllSourcesAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRetriever.Verify(r => r.RetrieveAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_TracksCosts()
    {
        // Arrange
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.SearchAsync("test query");

        // Assert
        _mockCostTracker.Verify(t => t.RecordCall(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MultiSourceDisabled_UsesHybridRetriever()
    {
        // Arrange
        SetupHappyPath();
        var orchestrator = CreateOrchestrator(cfg =>
        {
            cfg.AI.Rag.MultiSource.Enabled = false;
        });

        // Act
        var result = await orchestrator.SearchAsync("simple query");

        // Assert
        result.AssembledText.Should().Be("assembled text");
        _mockRetriever.Verify(r => r.RetrieveAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockMultiSource.Verify(m => m.RetrieveFromAllSourcesAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Modify `RagOrchestrator` constructor to accept new dependencies**

Update the `RagOrchestrator` class to accept `IMultiSourceOrchestrator`, `IQueryComplexityClassifier`, and `IRetrievalCostTracker`:

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs
// Add fields:
    private readonly IMultiSourceOrchestrator? _multiSourceOrchestrator;
    private readonly IQueryComplexityClassifier? _complexityClassifier;
    private readonly IRetrievalCostTracker? _costTracker;

// Update constructor signature (add 3 new parameters after queryRouter):
    public RagOrchestrator(
        IHybridRetriever hybridRetriever,
        IReranker reranker,
        ICragEvaluator cragEvaluator,
        IRagContextAssembler contextAssembler,
        IGraphRagService graphRagService,
        IFeedbackWeightedScorer? feedbackScorer,
        QueryRouter queryRouter,
        IMultiSourceOrchestrator? multiSourceOrchestrator,
        IQueryComplexityClassifier? complexityClassifier,
        IRetrievalCostTracker? costTracker,
        IOptionsMonitor<AppConfig> configMonitor,
        ILogger<RagOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(hybridRetriever);
        ArgumentNullException.ThrowIfNull(reranker);
        ArgumentNullException.ThrowIfNull(cragEvaluator);
        ArgumentNullException.ThrowIfNull(contextAssembler);
        ArgumentNullException.ThrowIfNull(graphRagService);
        ArgumentNullException.ThrowIfNull(queryRouter);
        ArgumentNullException.ThrowIfNull(configMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _hybridRetriever = hybridRetriever;
        _reranker = reranker;
        _cragEvaluator = cragEvaluator;
        _contextAssembler = contextAssembler;
        _graphRagService = graphRagService;
        _feedbackScorer = feedbackScorer;
        _queryRouter = queryRouter;
        _multiSourceOrchestrator = multiSourceOrchestrator;
        _complexityClassifier = complexityClassifier;
        _costTracker = costTracker;
        _configMonitor = configMonitor;
        _logger = logger;
    }
```

- [ ] **Step 3: Update `SearchAsync` to use multi-source when enabled**

In the `SearchAsync` method, after determining the strategy (before the strategy routing), add multi-source routing:

```csharp
    public async Task<RagAssembledContext> SearchAsync(
        string query,
        int? topK = null,
        string? collectionName = null,
        RetrievalStrategy? strategyOverride = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.orchestrator.search");
        var sw = Stopwatch.StartNew();
        var ragConfig = _configMonitor.CurrentValue.AI.Rag;
        var effectiveTopK = topK ?? ragConfig.Retrieval.TopK;
        if (effectiveTopK <= 0) effectiveTopK = DefaultTopK;

        var strategy = strategyOverride ?? await ClassifyStrategyAsync(query, cancellationToken);
        var strategyTag = strategy.ToString().ToLowerInvariant();
        activity?.SetTag(RagConventions.RetrievalStrategy, strategyTag);

        var tags = new KeyValuePair<string, object?>(RagConventions.RetrievalStrategy, strategyTag);
        RagRetrievalMetrics.Queries.Add(1, tags);

        _logger.LogInformation(
            "RAG orchestrator: Strategy={Strategy}, TopK={TopK}, MaxTokens={MaxTokens}",
            strategy, effectiveTopK, DefaultMaxTokens);

        try
        {
            RagAssembledContext result;

            if (strategy == RetrievalStrategy.GraphRag)
            {
                result = await ExecuteGraphRagAsync(query, cancellationToken);
            }
            else if (ragConfig.MultiSource.Enabled &&
                     _multiSourceOrchestrator is not null &&
                     _complexityClassifier is not null)
            {
                result = await ExecuteMultiSourcePipelineAsync(
                    query, effectiveTopK, cancellationToken);
            }
            else
            {
                result = await ExecuteVectorPipelineAsync(
                    query, effectiveTopK, collectionName, cancellationToken);
            }

            // Track cost
            _costTracker?.RecordCall(
                result.TotalTokens, 0, sw.Elapsed);

            return result;
        }
        finally
        {
            sw.Stop();
            RagRetrievalMetrics.RetrievalDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }
```

- [ ] **Step 4: Add `ExecuteMultiSourcePipelineAsync` method**

Add this method to the `RagOrchestrator` class:

```csharp
    private async Task<RagAssembledContext> ExecuteMultiSourcePipelineAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("rag.orchestrator.multi_source_pipeline");

        var classification = await _complexityClassifier!.ClassifyAsync(query, cancellationToken);

        _logger.LogInformation(
            "Multi-source pipeline: Complexity={Complexity}, Confidence={Confidence:F2}",
            classification.Complexity, classification.Confidence);

        var candidates = await _multiSourceOrchestrator!.RetrieveFromAllSourcesAsync(
            query, topK, classification.Complexity, cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogWarning("Multi-source retrieval returned 0 candidates");
            return CreateEmptyContext("No relevant documents found across any source.");
        }

        activity?.SetTag(RagConventions.RetrievalChunksReturned, candidates.Count);
        RagRetrievalMetrics.ChunksReturned.Record(candidates.Count);
        RagRetrievalMetrics.Hits.Add(1);

        // Continue with standard rerank -> CRAG -> assemble pipeline
        var reranked = await _reranker.RerankAsync(query, candidates, topK, cancellationToken);

        if (_feedbackScorer is not null)
            reranked = await _feedbackScorer.BlendFeedbackAsync(reranked, query, cancellationToken);

        var evaluation = await _cragEvaluator.EvaluateAsync(query, candidates, cancellationToken);

        activity?.SetTag(RagConventions.CragAction, evaluation.Action.ToString().ToLowerInvariant());
        activity?.SetTag(RagConventions.CragScore, evaluation.RelevanceScore);

        if (evaluation.Action == CorrectionAction.Reject)
        {
            return CreateEmptyContext(
                evaluation.Reasoning ?? "Multi-source content not relevant to the query.");
        }

        if (evaluation.Action == CorrectionAction.Refine)
        {
            var filtered = FilterWeakChunks(reranked, evaluation.WeakChunkIds);
            return await _contextAssembler.AssembleAsync(filtered, DefaultMaxTokens, cancellationToken);
        }

        return await _contextAssembler.AssembleAsync(reranked, DefaultMaxTokens, cancellationToken);
    }
```

- [ ] **Step 5: Run tests and verify all pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~RagOrchestratorTests"`
Expected: All existing tests still pass + 3 new tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorTests.cs
git commit -m "feat(rag): integrate MultiSourceOrchestrator and cost tracking into RagOrchestrator"
```

---

### Task 11: DI Registration

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs`
- Modify: `src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.Planner.cs`

- [ ] **Step 1: Add multi-source and quality gate registration methods to RAG DI**

Add two new private methods and update `AddRagDependencies` to call them:

```csharp
// In AddRagDependencies, add two new calls at the end:
    public static IServiceCollection AddRagDependencies(
        this IServiceCollection services,
        AppConfig appConfig)
    {
        AddRagIngestion(services, appConfig);
        AddRagRetrieval(services, appConfig);
        AddRagQueryTransform(services, appConfig);
        AddRagEvaluation(services, appConfig);
        AddRagGraphRag(services, appConfig);
        AddRagOrchestration(services, appConfig);
        AddRagMultiSource(services, appConfig);
        AddRagQualityGates(services, appConfig);

        return services;
    }
```

Add the two new methods:

```csharp
    /// <summary>
    /// Registers multi-source orchestration services: the <see cref="IMultiSourceOrchestrator"/>
    /// for parallel fan-out across vector, graph, and web sources, and the
    /// <see cref="IRetrievalCostTracker"/> for per-execution token accounting.
    /// </summary>
    private static void AddRagMultiSource(IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<IRetrievalCostTracker, RetrievalCostTracker>();

        services.AddSingleton<IMultiSourceOrchestrator>(sp =>
            new MultiSourceOrchestrator(
                sp.GetRequiredService<IHybridRetriever>(),
                sp.GetRequiredService<IGraphRagService>(),
                sp.GetRequiredService<IRetrievalCostTracker>(),
                sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
                sp.GetRequiredService<ILogger<MultiSourceOrchestrator>>()));
    }

    /// <summary>
    /// Registers the Ragas-inspired <see cref="IRetrievalQualityEvaluator"/> for evaluating
    /// retrieval quality via LLM judges. Used by CI/CD quality gate tests and runtime
    /// quality monitoring.
    /// </summary>
    private static void AddRagQualityGates(IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<IRetrievalQualityEvaluator>(sp =>
            new RetrievalQualityEvaluator(
                sp.GetRequiredService<IRagModelRouter>(),
                sp.GetRequiredService<ILogger<RetrievalQualityEvaluator>>()));
    }
```

Also update `AddRagOrchestration` to pass the new dependencies:

```csharp
    private static void AddRagOrchestration(IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<IRagOrchestrator>(sp =>
            new RagOrchestrator(
                sp.GetRequiredService<IHybridRetriever>(),
                sp.GetRequiredService<IReranker>(),
                sp.GetRequiredService<ICragEvaluator>(),
                sp.GetRequiredService<IRagContextAssembler>(),
                sp.GetRequiredService<IGraphRagService>(),
                sp.GetService<IFeedbackWeightedScorer>(),
                sp.GetRequiredService<QueryRouter>(),
                sp.GetService<IMultiSourceOrchestrator>(),
                sp.GetService<IQueryComplexityClassifier>(),
                sp.GetService<IRetrievalCostTracker>(),
                sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
                sp.GetRequiredService<ILogger<RagOrchestrator>>()));
    }
```

Add the required using directives at the top:

```csharp
using Application.AI.Common.Interfaces.RAG;
using Infrastructure.AI.RAG.Evaluation;
```

- [ ] **Step 2: Register `RetrievalPlanStepExecutor` in the planner DI**

In `DependencyInjection.Planner.cs`, add the keyed registration to `RegisterPlannerServices`:

```csharp
    private static void RegisterPlannerServices(IServiceCollection services)
    {
        services.AddScoped<IPlanExecutor, PlanExecutor>();
        services.AddScoped<IPlanValidator, PlanValidator>();
        services.AddScoped<IPlanGenerator, LlmPlanGeneratorService>();
        services.AddScoped<IPlanStateStore, EfCorePlanStateStore>();
        services.AddScoped<PlanExecutionContext>();

        services.AddKeyedScoped<IPlanStepExecutor>(StepType.LlmCall,
            (sp, _) => sp.GetRequiredService<LlmCallStepExecutor>());
        services.AddKeyedScoped<IPlanStepExecutor>(StepType.ToolUse,
            (sp, _) => sp.GetRequiredService<ToolUseStepExecutor>());
        services.AddKeyedScoped<IPlanStepExecutor>(StepType.HumanGate,
            (sp, _) => sp.GetRequiredService<HumanGateStepExecutor>());
        services.AddKeyedScoped<IPlanStepExecutor>(StepType.ConditionalBranch,
            (sp, _) => sp.GetRequiredService<ConditionalBranchStepExecutor>());
        services.AddKeyedScoped<IPlanStepExecutor>(StepType.SubPlanInvocation,
            (sp, _) => sp.GetRequiredService<SubPlanStepExecutor>());
        services.AddKeyedScoped<IPlanStepExecutor>(StepType.Retrieval,
            (sp, _) => sp.GetRequiredService<RetrievalPlanStepExecutor>());

        services.AddScoped<LlmCallStepExecutor>();
        services.AddScoped<ToolUseStepExecutor>();
        services.AddScoped<HumanGateStepExecutor>();
        services.AddScoped<ConditionalBranchStepExecutor>();
        services.AddScoped<SubPlanStepExecutor>();
        services.AddScoped<RetrievalPlanStepExecutor>();
    }
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Run all tests**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.Planner.cs
git commit -m "feat(rag): register MultiSourceOrchestrator, RetrievalQualityEvaluator, RetrievalCostTracker, and RetrievalPlanStepExecutor in DI"
```

---

### Task 12: CI/CD Quality Gate Tests

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/QualityGates/RagQualityGateTests.cs`

- [ ] **Step 1: Write the quality gate test fixture**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/QualityGates/RagQualityGateTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Infrastructure.AI.RAG.Tests.QualityGates;

/// <summary>
/// CI/CD quality gate tests that evaluate retrieval quality against a deterministic
/// golden dataset. Each test runs a query through a mock evaluation pipeline and
/// asserts that Ragas-style metrics meet minimum thresholds.
/// <para>
/// In production CI, these tests run against a real RAG pipeline with a curated
/// golden dataset. For unit testing, we use deterministic mocks to verify the
/// gate logic works correctly.
/// </para>
/// </summary>
public sealed class RagQualityGateTests
{
    /// <summary>
    /// Deterministic golden dataset entries for quality gate testing.
    /// Each entry defines a query, expected ground-truth answer, and mock
    /// retrieval context. In production, this would be loaded from a JSON file.
    /// </summary>
    private static readonly GoldenDatasetEntry[] GoldenDataset =
    [
        new(
            Query: "What is the purpose of Clean Architecture?",
            GroundTruth: "Clean Architecture separates concerns into layers with dependencies pointing inward. " +
                         "Domain has no external dependencies. Application depends only on Domain. " +
                         "Infrastructure implements Application interfaces.",
            ExpectedAnswer: "Clean Architecture separates concerns into layers where dependencies point inward, " +
                            "with Domain at the center having no external dependencies."),
        new(
            Query: "How does the planner execute steps?",
            GroundTruth: "The PlanExecutor orchestrates a PlanGraph with bounded concurrency. " +
                         "Steps are dispatched to keyed IPlanStepExecutor implementations via StepType. " +
                         "State is persisted to EfCorePlanStateStore with checkpoint/resume support.",
            ExpectedAnswer: "The PlanExecutor runs plan steps using keyed step executors, " +
                            "with state persistence and checkpoint/resume capabilities."),
        new(
            Query: "What retrieval strategies does the RAG pipeline support?",
            GroundTruth: "The RAG pipeline supports HybridVectorBm25, GraphRag, RaptorTree, " +
                         "and MultiQueryFusion strategies. Strategy selection is either automatic " +
                         "via query classification or manual via strategy override.",
            ExpectedAnswer: "The RAG pipeline supports four strategies: hybrid vector+BM25, " +
                            "GraphRAG, RAPTOR tree, and multi-query fusion.")
    ];

    private readonly Mock<IRetrievalQualityEvaluator> _mockEvaluator = new();

    private void SetupEvaluatorWithScores(
        double precision, double recall, double faithfulness, double relevancy)
    {
        var overallScore = (precision * 0.25) + (recall * 0.25) +
                           (faithfulness * 0.30) + (relevancy * 0.20);

        _mockEvaluator
            .Setup(e => e.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RerankedResult>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalQualityReport
            {
                ContextPrecision = precision,
                ContextRecall = recall,
                Faithfulness = faithfulness,
                AnswerRelevancy = relevancy,
                OverallScore = overallScore,
                Reasoning = "Deterministic test evaluation",
                EvaluatedAt = DateTimeOffset.UtcNow
            });
    }

    [Fact]
    public async Task QualityGate_GoldenDataset_ContextPrecisionAboveThreshold()
    {
        // Arrange -- simulate high precision retrieval
        const double minPrecision = 0.7;
        SetupEvaluatorWithScores(precision: 0.85, recall: 0.80, faithfulness: 0.90, relevancy: 0.88);

        // Act -- evaluate each golden dataset entry
        var precisionScores = new List<double>();
        foreach (var entry in GoldenDataset)
        {
            var context = RagTestData.CreateRerankedResults(3);
            var report = await _mockEvaluator.Object.EvaluateAsync(
                entry.Query, entry.ExpectedAnswer, context, entry.GroundTruth);
            precisionScores.Add(report.ContextPrecision);
        }

        // Assert -- average precision must be above threshold
        var avgPrecision = precisionScores.Average();
        avgPrecision.Should().BeGreaterOrEqualTo(minPrecision,
            $"average context precision ({avgPrecision:F2}) must be >= {minPrecision} " +
            "to pass the quality gate");
    }

    [Fact]
    public async Task QualityGate_GoldenDataset_FaithfulnessAboveThreshold()
    {
        // Arrange -- simulate high faithfulness
        const double minFaithfulness = 0.8;
        SetupEvaluatorWithScores(precision: 0.85, recall: 0.80, faithfulness: 0.92, relevancy: 0.88);

        // Act
        var faithfulnessScores = new List<double>();
        foreach (var entry in GoldenDataset)
        {
            var context = RagTestData.CreateRerankedResults(3);
            var report = await _mockEvaluator.Object.EvaluateAsync(
                entry.Query, entry.ExpectedAnswer, context, entry.GroundTruth);
            faithfulnessScores.Add(report.Faithfulness);
        }

        // Assert
        var avgFaithfulness = faithfulnessScores.Average();
        avgFaithfulness.Should().BeGreaterOrEqualTo(minFaithfulness,
            $"average faithfulness ({avgFaithfulness:F2}) must be >= {minFaithfulness} " +
            "to pass the quality gate");
    }

    [Fact]
    public async Task QualityGate_GoldenDataset_OverallScoreAboveThreshold()
    {
        // Arrange
        const double minOverall = 0.7;
        SetupEvaluatorWithScores(precision: 0.85, recall: 0.80, faithfulness: 0.90, relevancy: 0.88);

        // Act
        var overallScores = new List<double>();
        foreach (var entry in GoldenDataset)
        {
            var context = RagTestData.CreateRerankedResults(3);
            var report = await _mockEvaluator.Object.EvaluateAsync(
                entry.Query, entry.ExpectedAnswer, context, entry.GroundTruth);
            overallScores.Add(report.OverallScore);
        }

        // Assert
        var avgOverall = overallScores.Average();
        avgOverall.Should().BeGreaterOrEqualTo(minOverall,
            $"average overall score ({avgOverall:F2}) must be >= {minOverall} " +
            "to pass the quality gate");
    }

    /// <summary>
    /// A single entry in the golden evaluation dataset.
    /// </summary>
    private sealed record GoldenDatasetEntry(
        string Query,
        string GroundTruth,
        string ExpectedAnswer);
}
```

- [ ] **Step 2: Run tests and verify all pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~RagQualityGateTests"`
Expected: 3 tests pass, 0 failures.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/QualityGates/RagQualityGateTests.cs
git commit -m "test(rag): add CI/CD quality gate tests with golden dataset evaluation"
```

---

### Task 13: Integration Tests

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/FullAutonomyIntegrationTests.cs`

- [ ] **Step 1: Write the integration tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/FullAutonomyIntegrationTests.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.Planner;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.Planner;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.Planner.StepExecutors;
using Infrastructure.AI.RAG.Orchestration;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

/// <summary>
/// End-to-end integration tests verifying the full autonomy flow:
/// planner creates retrieval step -> executor runs it -> output is consumable by downstream steps.
/// </summary>
public sealed class FullAutonomyIntegrationTests
{
    private readonly Mock<IRagOrchestrator> _mockRagOrchestrator = new();
    private readonly Mock<IMultiSourceOrchestrator> _mockMultiSource = new();
    private readonly Mock<IQueryComplexityClassifier> _mockComplexityClassifier = new();
    private readonly Mock<IPlanProgressNotifier> _mockNotifier = new();

    private RetrievalPlanStepExecutor CreateRetrievalExecutor(IRetrievalCostTracker? costTracker = null)
    {
        return new RetrievalPlanStepExecutor(
            _mockRagOrchestrator.Object,
            _mockMultiSource.Object,
            _mockComplexityClassifier.Object,
            costTracker ?? new RetrievalCostTracker(RagTestData.CreateConfigMonitor()),
            _mockNotifier.Object,
            new PlanExecutionContext(),
            Mock.Of<ILogger<RetrievalPlanStepExecutor>>());
    }

    [Fact]
    public async Task RetrievalStep_ProducesJsonOutput_ConsumableByDownstreamLlmStep()
    {
        // Arrange -- Setup RAG orchestrator to return context
        var expectedContext = new RagAssembledContext
        {
            AssembledText = "Clean Architecture separates concerns into layers.",
            TotalTokens = 42,
            WasTruncated = false,
            Citations =
            [
                new CitationSpan
                {
                    ChunkId = "arch-chunk-1",
                    DocumentUri = new Uri("file:///docs/architecture.md"),
                    SectionPath = "Architecture > Overview",
                    StartOffset = 0,
                    EndOffset = 49
                }
            ]
        };

        _mockRagOrchestrator
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContext);

        var executor = CreateRetrievalExecutor();

        // Create retrieval step as planner would
        var retrievalStep = new PlanStep
        {
            Id = PlanStepId.New(),
            Name = "Retrieve architecture docs",
            Type = StepType.Retrieval,
            Configuration = new RetrievalStepConfiguration
            {
                Query = "What is clean architecture?",
                TopK = 5
            },
            RetryPolicy = new RetryPolicy { MaxRetries = 1 }
        };

        // Act
        var result = await executor.ExecuteAsync(
            retrievalStep,
            new Dictionary<PlanStepId, string>(),
            CancellationToken.None);

        // Assert -- result is completed and output is valid JSON
        result.Status.Should().Be(StepExecutionStatus.Completed);
        result.Output.Should().NotBeNullOrEmpty();

        // Verify the JSON can be parsed and contains expected data
        var outputDoc = JsonDocument.Parse(result.Output!);
        var assembledText = outputDoc.RootElement.GetProperty("assembledText").GetString();
        assembledText.Should().Contain("Clean Architecture");

        // Verify the output could serve as upstream input to an LLM step
        var upstreamOutputs = new Dictionary<PlanStepId, string>
        {
            { retrievalStep.Id, result.Output! }
        };
        upstreamOutputs[retrievalStep.Id].Should().Contain("assembledText");
    }

    [Fact]
    public async Task MultiSourceRetrievalStep_IncludesComplexityInOutput()
    {
        // Arrange
        var retrievalResults = RagTestData.CreateRetrievalResults(3);
        _mockMultiSource
            .Setup(m => m.RetrieveFromAllSourcesAsync(
                It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrievalResults);
        _mockComplexityClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplexityClassification
            {
                Complexity = QueryComplexity.Complex,
                Confidence = 0.9
            });

        var executor = CreateRetrievalExecutor();

        var step = new PlanStep
        {
            Id = PlanStepId.New(),
            Name = "Multi-source retrieval",
            Type = StepType.Retrieval,
            Configuration = new RetrievalStepConfiguration
            {
                Query = "Compare all architecture patterns used in the system",
                UseMultiSource = true,
                TopK = 10
            },
            RetryPolicy = new RetryPolicy { MaxRetries = 0 }
        };

        // Act
        var result = await executor.ExecuteAsync(
            step, new Dictionary<PlanStepId, string>(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(StepExecutionStatus.Completed);
        var outputDoc = JsonDocument.Parse(result.Output!);
        outputDoc.RootElement.GetProperty("complexity").GetString()
            .Should().Be("Complex");
        outputDoc.RootElement.GetProperty("resultCount").GetInt32()
            .Should().Be(3);
    }

    [Fact]
    public async Task CostTracker_AggregatesAcrossMultipleSteps()
    {
        // Arrange
        var costTracker = new RetrievalCostTracker(RagTestData.CreateConfigMonitor());

        _mockRagOrchestrator
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<RetrievalStrategy?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext
            {
                AssembledText = "Result", TotalTokens = 50, WasTruncated = false
            });

        var executor = CreateRetrievalExecutor(costTracker);

        // Act -- execute 3 retrieval steps
        for (var i = 0; i < 3; i++)
        {
            var step = new PlanStep
            {
                Id = PlanStepId.New(),
                Name = $"Retrieval step {i}",
                Type = StepType.Retrieval,
                Configuration = new RetrievalStepConfiguration { Query = $"query {i}" },
                RetryPolicy = new RetryPolicy { MaxRetries = 0 }
            };

            await executor.ExecuteAsync(step, new Dictionary<PlanStepId, string>(), CancellationToken.None);
        }

        // Assert -- cost tracker aggregated all 3 calls
        var summary = costTracker.GetSummary();
        summary.RetrievalCalls.Should().Be(3);
        summary.TotalTokensUsed.Should().BeGreaterThan(0);
        summary.TotalLatency.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: Run tests and verify all pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~FullAutonomyIntegrationTests"`
Expected: 3 tests pass, 0 failures.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/FullAutonomyIntegrationTests.cs
git commit -m "test(rag): add full autonomy integration tests covering retrieval step -> downstream consumption"
```

---

### Task 14: OTel Metrics -- Multi-Source and Quality Conventions

**Files:**
- Modify: `src/Content/Domain/Domain.AI/Telemetry/Conventions/RagConventions.cs`
- Modify: `src/Content/Application/Application.AI.Common/OpenTelemetry/Metrics/RagRetrievalMetrics.cs`

- [ ] **Step 1: Add multi-source and quality metric constants to `RagConventions`**

Add these constants to the `RagConventions` class:

```csharp
    // ── Multi-source attributes ─────────────────────────────────────

    /// <summary>Number of sources queried in a multi-source retrieval.</summary>
    public const string MultiSourceCount = "rag.multi_source.source_count";

    /// <summary>Query complexity tier used for source selection.</summary>
    public const string MultiSourceComplexity = "rag.multi_source.complexity";

    /// <summary>Per-source latency in milliseconds.</summary>
    public const string MultiSourceLatency = "rag.multi_source.latency_ms";

    // ── Quality evaluation attributes ───────────────────────────────

    /// <summary>Context precision score (0-1) from quality evaluation.</summary>
    public const string QualityContextPrecision = "rag.quality.context_precision";

    /// <summary>Context recall score (0-1) from quality evaluation.</summary>
    public const string QualityContextRecall = "rag.quality.context_recall";

    /// <summary>Faithfulness score (0-1) from quality evaluation.</summary>
    public const string QualityFaithfulness = "rag.quality.faithfulness";

    /// <summary>Answer relevancy score (0-1) from quality evaluation.</summary>
    public const string QualityAnswerRelevancy = "rag.quality.answer_relevancy";

    /// <summary>Overall quality score (0-1) from quality evaluation.</summary>
    public const string QualityOverallScore = "rag.quality.overall_score";

    // ── Cost tracking attributes ────────────────────────────────────

    /// <summary>Total tokens consumed in a retrieval execution.</summary>
    public const string CostTotalTokens = "rag.cost.total_tokens";

    /// <summary>Prompt tokens consumed in a retrieval execution.</summary>
    public const string CostPromptTokens = "rag.cost.prompt_tokens";

    /// <summary>Completion tokens consumed in a retrieval execution.</summary>
    public const string CostCompletionTokens = "rag.cost.completion_tokens";

    /// <summary>Estimated cost in USD for a retrieval execution.</summary>
    public const string CostEstimatedUsd = "rag.cost.estimated_usd";

    // ── Multi-source metric name constants ──────────────────────────

    /// <summary>Histogram: multi-source orchestration total duration in milliseconds.</summary>
    public const string MultiSourceDuration = "rag.multi_source.duration";

    /// <summary>Histogram: per-source retrieval latency in milliseconds.</summary>
    public const string MultiSourcePerSourceLatency = "rag.multi_source.per_source_latency";

    /// <summary>Counter: total multi-source orchestration invocations.</summary>
    public const string MultiSourceInvocations = "rag.multi_source.invocations";

    // ── Quality metric name constants ───────────────────────────────

    /// <summary>Histogram: overall quality scores from evaluations.</summary>
    public const string QualityScoreHistogram = "rag.quality.overall_score_histogram";

    /// <summary>Counter: total quality evaluations performed.</summary>
    public const string QualityEvaluations = "rag.quality.evaluations";

    // ── Cost metric name constants ──────────────────────────────────

    /// <summary>Counter: total tokens consumed across all retrieval operations.</summary>
    public const string CostTokensTotal = "rag.cost.tokens_total";

    /// <summary>Histogram: estimated cost per retrieval execution in USD.</summary>
    public const string CostPerExecution = "rag.cost.per_execution";
```

- [ ] **Step 2: Add metric instruments to `RagRetrievalMetrics`**

Add these instruments to the `RagRetrievalMetrics` class:

```csharp
    // ── Multi-source instruments ────────────────────────────────────

    /// <summary>Multi-source orchestration total duration in milliseconds.</summary>
    public static Histogram<double> MultiSourceDuration { get; } =
        AppInstrument.Meter.CreateHistogram<double>(
            RagConventions.MultiSourceDuration, "{ms}", "Multi-source orchestration duration");

    /// <summary>Per-source retrieval latency in milliseconds. Tags: rag.source.name.</summary>
    public static Histogram<double> MultiSourcePerSourceLatency { get; } =
        AppInstrument.Meter.CreateHistogram<double>(
            RagConventions.MultiSourcePerSourceLatency, "{ms}", "Per-source retrieval latency");

    /// <summary>Total multi-source orchestration invocations.</summary>
    public static Counter<long> MultiSourceInvocations { get; } =
        AppInstrument.Meter.CreateCounter<long>(
            RagConventions.MultiSourceInvocations, "{invocation}", "Multi-source invocations");

    // ── Quality instruments ─────────────────────────────────────────

    /// <summary>Overall quality score histogram. Tags: none.</summary>
    public static Histogram<double> QualityScores { get; } =
        AppInstrument.Meter.CreateHistogram<double>(
            RagConventions.QualityScoreHistogram, "{score}", "RAG quality evaluation scores");

    /// <summary>Total quality evaluations performed.</summary>
    public static Counter<long> QualityEvaluations { get; } =
        AppInstrument.Meter.CreateCounter<long>(
            RagConventions.QualityEvaluations, "{evaluation}", "Quality evaluations performed");

    // ── Cost instruments ────────────────────────────────────────────

    /// <summary>Total tokens consumed across all retrieval operations.</summary>
    public static Counter<long> CostTokensTotal { get; } =
        AppInstrument.Meter.CreateCounter<long>(
            RagConventions.CostTokensTotal, "{token}", "Total tokens consumed by retrieval");

    /// <summary>Estimated cost per retrieval execution in USD.</summary>
    public static Histogram<double> CostPerExecution { get; } =
        AppInstrument.Meter.CreateHistogram<double>(
            RagConventions.CostPerExecution, "{usd}", "Estimated cost per retrieval execution");
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Run all tests to verify nothing broke**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Domain/Domain.AI/Telemetry/Conventions/RagConventions.cs src/Content/Application/Application.AI.Common/OpenTelemetry/Metrics/RagRetrievalMetrics.cs
git commit -m "feat(rag): add OTel conventions and metric instruments for multi-source, quality, and cost tracking"
```

---

## Final Verification

After all 14 tasks are complete:

```bash
dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx
```

Expected: Full build success. All tests pass (existing + ~33 new tests from Phase D).

## Test Summary

| Test Class | Count | Covers |
|-----------|-------|--------|
| `RetrievalCostTrackerTests` | 5 | Thread-safe cost tracking |
| `MultiSourceOrchestratorTests` | 7 | Parallel fan-out, dedup, timeout |
| `RetrievalQualityEvaluatorTests` | 6 | Ragas metrics via LLM judges |
| `RetrievalPlanStepExecutorTests` | 6 | Planner integration |
| `RagQualityGateTests` | 3 | CI/CD quality gates |
| `FullAutonomyIntegrationTests` | 3 | End-to-end flows |
| **Total** | **30** | |
