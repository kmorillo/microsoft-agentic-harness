# Phase B: Multi-Hop & Reflection --- Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable the RAG pipeline to answer complex multi-part questions by decomposing them into ordered sub-queries, retrieving iteratively with sufficiency checks, and validating answer faithfulness against retrieved context.

**Architecture:** A `QueryDecomposer` uses LLM few-shot prompting to split complex queries into dependency-ordered sub-queries. An `IterativeRetriever` orchestrates a multi-hop loop: for each sub-query (in dependency order), retrieve via `IHybridRetriever`, evaluate sufficiency via `SufficiencyEvaluator`, and refine if insufficient --- up to a configurable `MaxHops` cap with token budget enforcement. After context assembly, an `AnswerFaithfulnessEvaluator` checks whether the assembled answer is grounded in the retrieved context and flags hallucinated claims for corrective action.

**Tech Stack:** C# .NET 10, Microsoft.Extensions.AI (IChatClient), xUnit + Moq + FluentAssertions, keyed DI

**Depends on:** Phase A (Adaptive Routing) --- complexity classification determines when to trigger multi-hop.

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `src/Content/Domain/Domain.AI/RAG/Models/SubQuery.cs` | Sub-query with dependency tracking |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/DecomposedQuery.cs` | Decomposition result |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/HopResult.cs` | Single hop retrieval result |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/IterativeRetrievalResult.cs` | Multi-hop aggregate result |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/FaithfulnessEvaluation.cs` | Faithfulness check result |
| Create | `src/Content/Domain/Domain.Common/Config/AI/RAG/MultiHopConfig.cs` | Multi-hop retrieval config |
| Create | `src/Content/Domain/Domain.Common/Config/AI/RAG/FaithfulnessConfig.cs` | Faithfulness evaluation config |
| Modify | `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs` | Add MultiHop + Faithfulness sections |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryDecomposer.cs` | Query decomposition interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/ISufficiencyEvaluator.cs` | Context sufficiency check interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IIterativeRetriever.cs` | Multi-hop retrieval interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IAnswerFaithfulnessEvaluator.cs` | Answer faithfulness interface |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryDecomposer.cs` | LLM-based query decomposition |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/SufficiencyEvaluator.cs` | LLM sufficiency evaluation |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/IterativeRetriever.cs` | Multi-hop retrieval loop |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/AnswerFaithfulnessEvaluator.cs` | LLM faithfulness evaluation |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs` | Add multi-hop path for Complex tier |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs` | Register multi-hop & faithfulness services |
| Modify | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs` | Add multi-hop test data builders |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryDecomposerTests.cs` | Decomposer tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/SufficiencyEvaluatorTests.cs` | Sufficiency evaluator tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Retrieval/IterativeRetrieverTests.cs` | Iterative retriever tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/AnswerFaithfulnessEvaluatorTests.cs` | Faithfulness evaluator tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiHopIntegrationTests.cs` | End-to-end multi-hop tests |

---

### Task 1: Domain Models --- SubQuery, DecomposedQuery, HopResult, IterativeRetrievalResult, FaithfulnessEvaluation

**Files:**
- Create: `src/Content/Domain/Domain.AI/RAG/Models/SubQuery.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/DecomposedQuery.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/HopResult.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/IterativeRetrievalResult.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/FaithfulnessEvaluation.cs`

- [ ] **Step 1: Create the SubQuery record**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/SubQuery.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// A single sub-query produced by decomposing a complex query into smaller,
/// independently-retrievable parts. Each sub-query has an execution order
/// and may declare dependencies on other sub-queries whose results must be
/// available before this sub-query can be meaningfully answered.
/// </summary>
/// <remarks>
/// <para>
/// Dependencies are expressed as order indices, not IDs, because the decomposer
/// assigns sequential orders during decomposition. For example, a sub-query at
/// <c>Order = 3</c> with <c>DependsOnOrders = [1, 2]</c> means "execute this only
/// after sub-queries 1 and 2 have completed, and include their results as context."
/// </para>
/// </remarks>
public sealed record SubQuery
{
    /// <summary>
    /// The natural language text of this sub-query, suitable for direct retrieval.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// The 1-based execution order of this sub-query within the decomposition.
    /// Sub-queries with no dependencies can execute in any order; those with
    /// dependencies must wait for their prerequisites.
    /// </summary>
    public required int Order { get; init; }

    /// <summary>
    /// The orders of sub-queries that must complete before this one executes.
    /// Empty means this sub-query has no dependencies and can execute immediately.
    /// </summary>
    public IReadOnlyList<int> DependsOnOrders { get; init; } = [];
}
```

- [ ] **Step 2: Create the DecomposedQuery record**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/DecomposedQuery.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// The result of decomposing a complex query into ordered sub-queries.
/// Contains the original query text and the decomposed sub-queries with
/// their dependency relationships.
/// </summary>
/// <remarks>
/// <para>
/// When a query cannot be decomposed (simple lookup, single-hop), the decomposer
/// returns a <see cref="DecomposedQuery"/> with a single <see cref="SubQuery"/>
/// wrapping the original query text at <c>Order = 1</c> with no dependencies.
/// </para>
/// </remarks>
public sealed record DecomposedQuery
{
    /// <summary>
    /// The original user query before decomposition.
    /// </summary>
    public required string OriginalQuery { get; init; }

    /// <summary>
    /// The ordered list of sub-queries produced by decomposition.
    /// Guaranteed to contain at least one sub-query.
    /// </summary>
    public required IReadOnlyList<SubQuery> SubQueries { get; init; }

    /// <summary>
    /// Whether any sub-query has dependencies on another, requiring sequential
    /// execution rather than parallel retrieval. Computed from the dependency graph.
    /// </summary>
    public bool RequiresSequentialExecution =>
        SubQueries.Any(sq => sq.DependsOnOrders.Count > 0);
}
```

- [ ] **Step 3: Create the HopResult record**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/HopResult.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// The result of a single retrieval hop within the iterative retrieval loop.
/// Captures the sub-query that triggered the hop, the retrieved results,
/// the sufficiency evaluation score, and whether the hop was deemed sufficient.
/// </summary>
public sealed record HopResult
{
    /// <summary>
    /// The sub-query that was used for retrieval in this hop.
    /// </summary>
    public required SubQuery SubQuery { get; init; }

    /// <summary>
    /// The retrieval results obtained for this hop's sub-query.
    /// </summary>
    public required IReadOnlyList<RetrievalResult> Results { get; init; }

    /// <summary>
    /// The sufficiency score (0.0 to 1.0) indicating how well the retrieved
    /// context answers the sub-query. Scores above the configured threshold
    /// indicate sufficient context; below triggers refinement or additional hops.
    /// </summary>
    public required double SufficiencyScore { get; init; }

    /// <summary>
    /// The 1-based hop number within the iterative retrieval sequence.
    /// </summary>
    public required int HopNumber { get; init; }

    /// <summary>
    /// Whether the retrieved context was deemed sufficient to answer the sub-query
    /// based on the configured <c>MinSufficiencyScore</c> threshold.
    /// </summary>
    public required bool IsSufficient { get; init; }
}
```

- [ ] **Step 4: Create the IterativeRetrievalResult record**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/IterativeRetrievalResult.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// The aggregate result of multi-hop iterative retrieval, containing all hop results,
/// the deduplicated aggregated retrieval results, token accounting, and budget status.
/// </summary>
public sealed record IterativeRetrievalResult
{
    /// <summary>
    /// The ordered list of hop results from each iteration of the retrieval loop.
    /// </summary>
    public required IReadOnlyList<HopResult> Hops { get; init; }

    /// <summary>
    /// All retrieval results aggregated across all hops, deduplicated by chunk ID.
    /// Results from earlier hops appear first; duplicates are resolved by keeping
    /// the instance with the highest fused score.
    /// </summary>
    public required IReadOnlyList<RetrievalResult> AggregatedResults { get; init; }

    /// <summary>
    /// The total token count consumed across all hops, used for budget enforcement.
    /// </summary>
    public required int TotalTokensUsed { get; init; }

    /// <summary>
    /// Whether the token budget was exhausted before all sub-queries completed,
    /// indicating that some sub-queries may have partial or no results.
    /// </summary>
    public required bool BudgetExhausted { get; init; }
}
```

- [ ] **Step 5: Create the FaithfulnessEvaluation record**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/FaithfulnessEvaluation.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// The result of evaluating whether an answer is faithful to the retrieved context.
/// Identifies specific hallucinated claims not supported by the context and claims
/// that are properly grounded, enabling corrective action on unfaithful answers.
/// </summary>
public sealed record FaithfulnessEvaluation
{
    /// <summary>
    /// Whether the answer is considered faithful overall. True when the
    /// <see cref="Score"/> is above the configured hallucination threshold.
    /// </summary>
    public required bool IsFaithful { get; init; }

    /// <summary>
    /// The faithfulness score (0.0 to 1.0) where 1.0 means fully grounded
    /// in the retrieved context and 0.0 means entirely hallucinated.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Specific claims in the answer that are not supported by the retrieved context.
    /// Each entry is a verbatim or paraphrased claim identified as hallucinated.
    /// </summary>
    public IReadOnlyList<string> HallucinatedClaims { get; init; } = [];

    /// <summary>
    /// Specific claims in the answer that are supported by the retrieved context.
    /// Each entry is a verbatim or paraphrased claim verified against the source.
    /// </summary>
    public IReadOnlyList<string> SupportedClaims { get; init; } = [];

    /// <summary>
    /// Optional reasoning from the evaluator explaining the faithfulness assessment.
    /// </summary>
    public string? Reasoning { get; init; }
}
```

- [ ] **Step 6: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Content/Domain/Domain.AI/RAG/Models/SubQuery.cs src/Content/Domain/Domain.AI/RAG/Models/DecomposedQuery.cs src/Content/Domain/Domain.AI/RAG/Models/HopResult.cs src/Content/Domain/Domain.AI/RAG/Models/IterativeRetrievalResult.cs src/Content/Domain/Domain.AI/RAG/Models/FaithfulnessEvaluation.cs
git commit -m "feat(rag): add multi-hop and faithfulness domain models"
```

---

### Task 2: Configuration --- MultiHopConfig and FaithfulnessConfig

**Files:**
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/MultiHopConfig.cs`
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/FaithfulnessConfig.cs`
- Modify: `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs`

- [ ] **Step 1: Create MultiHopConfig**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/MultiHopConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for multi-hop iterative retrieval. Controls the maximum number
/// of retrieval iterations, per-hop token budgets, sufficiency thresholds, and
/// the number of results retrieved per hop.
/// </summary>
/// <remarks>
/// <para>
/// Multi-hop retrieval is triggered when the Phase A complexity classifier
/// assigns <c>QueryComplexity.Complex</c>. The iterative retriever decomposes
/// the query, retrieves per sub-query, evaluates sufficiency, and refines
/// insufficient sub-queries up to <see cref="MaxHops"/> times.
/// </para>
/// </remarks>
public sealed class MultiHopConfig
{
    /// <summary>
    /// Enable multi-hop iterative retrieval for complex queries.
    /// When false, complex queries use the standard single-pass pipeline.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of retrieval iterations (hops) before stopping.
    /// Acts as a hard safety cap to prevent infinite retrieval loops.
    /// </summary>
    public int MaxHops { get; set; } = 3;

    /// <summary>
    /// Token budget allocated per individual hop. Limits the total content
    /// retrieved in each iteration to prevent context window overflow.
    /// </summary>
    public int TokenBudgetPerHop { get; set; } = 1024;

    /// <summary>
    /// Minimum sufficiency score (0.0 to 1.0) required to consider a sub-query
    /// answered. Below this threshold, the retriever refines and retries.
    /// </summary>
    public double MinSufficiencyScore { get; set; } = 0.7;

    /// <summary>
    /// Number of results to retrieve per hop. Lower values reduce cost;
    /// higher values increase recall for complex sub-queries.
    /// </summary>
    public int TopKPerHop { get; set; } = 5;
}
```

- [ ] **Step 2: Create FaithfulnessConfig**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/FaithfulnessConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for post-assembly answer faithfulness evaluation.
/// Controls whether answers are checked for hallucination and the
/// thresholds for triggering corrective action.
/// </summary>
/// <remarks>
/// <para>
/// Faithfulness evaluation runs after the context assembler produces the
/// final assembled text. If the evaluator detects hallucinated claims
/// exceeding the threshold, the orchestrator triggers a CRAG-style
/// refinement to improve grounding.
/// </para>
/// </remarks>
public sealed class FaithfulnessConfig
{
    /// <summary>
    /// Enable post-assembly faithfulness evaluation. When false, the assembled
    /// context is returned without hallucination checking.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Faithfulness score threshold (0.0 to 1.0) below which the answer is
    /// considered unfaithful and triggers corrective action. Lower values
    /// are more permissive; higher values demand stronger grounding.
    /// </summary>
    public double HallucinationThreshold { get; set; } = 0.3;

    /// <summary>
    /// Whether to require that every claim in the answer has explicit citation
    /// support from the retrieved context. When true, unsupported claims are
    /// treated as hallucinated even if they are factually correct.
    /// </summary>
    public bool RequireCitationSupport { get; set; } = true;
}
```

- [ ] **Step 3: Add MultiHop and Faithfulness properties to RagConfig**

In `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs`, add these two properties after the existing `ModelTiering` property:

```csharp
/// <summary>
/// Gets or sets the multi-hop iterative retrieval configuration for
/// complex queries requiring decomposition and sequential retrieval.
/// </summary>
public MultiHopConfig MultiHop { get; set; } = new();

/// <summary>
/// Gets or sets the faithfulness evaluation configuration for
/// post-assembly hallucination detection and corrective action.
/// </summary>
public FaithfulnessConfig Faithfulness { get; set; } = new();
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Domain/Domain.Common/Config/AI/RAG/MultiHopConfig.cs src/Content/Domain/Domain.Common/Config/AI/RAG/FaithfulnessConfig.cs src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs
git commit -m "feat(rag): add MultiHopConfig and FaithfulnessConfig"
```

---

### Task 3: Application Interfaces --- IQueryDecomposer, ISufficiencyEvaluator, IIterativeRetriever, IAnswerFaithfulnessEvaluator

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryDecomposer.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/ISufficiencyEvaluator.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IIterativeRetriever.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IAnswerFaithfulnessEvaluator.cs`

- [ ] **Step 1: Create IQueryDecomposer**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryDecomposer.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Decomposes complex queries into ordered sub-queries with dependency tracking.
/// Used by the iterative retriever to break multi-hop questions into independently
/// retrievable parts that can be answered sequentially.
/// </summary>
/// <remarks>
/// <para><strong>Implementation guidance:</strong></para>
/// <list type="bullet">
///   <item>Use LLM few-shot prompting to decompose queries.</item>
///   <item>For non-decomposable queries, return a single sub-query wrapping the original.</item>
///   <item>Assign sequential 1-based orders to sub-queries.</item>
///   <item>Set <c>DependsOnOrders</c> when a sub-query requires context from prior sub-queries.</item>
///   <item>On LLM failure, fall back to a single sub-query (never throw).</item>
///   <item>Route to <c>"query_decomposition"</c> operation via <see cref="IRagModelRouter"/>.</item>
/// </list>
/// </remarks>
public interface IQueryDecomposer
{
    /// <summary>
    /// Decompose a complex query into ordered sub-queries.
    /// </summary>
    /// <param name="query">The original user query to decompose.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DecomposedQuery"/> containing the original query and its sub-queries.
    /// Guaranteed to contain at least one sub-query.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="query"/> is null or empty.</exception>
    Task<DecomposedQuery> DecomposeAsync(string query, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create ISufficiencyEvaluator**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/ISufficiencyEvaluator.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Evaluates whether retrieved context is sufficient to answer a sub-query.
/// Returns a score between 0.0 (completely insufficient) and 1.0 (fully sufficient).
/// Used by the iterative retriever to decide whether to refine and retry.
/// </summary>
/// <remarks>
/// <para><strong>Implementation guidance:</strong></para>
/// <list type="bullet">
///   <item>Use LLM prompting to evaluate sufficiency contextually.</item>
///   <item>Return 0.0 for empty result sets without calling the LLM.</item>
///   <item>On LLM failure, return a default score of 0.5 (uncertain --- triggers retry).</item>
///   <item>Route to <c>"sufficiency_evaluation"</c> operation via <see cref="IRagModelRouter"/>.</item>
/// </list>
/// </remarks>
public interface ISufficiencyEvaluator
{
    /// <summary>
    /// Evaluate whether the retrieved results sufficiently answer the given sub-query.
    /// </summary>
    /// <param name="subQuery">The sub-query to evaluate against.</param>
    /// <param name="results">The retrieval results to assess for sufficiency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A score between 0.0 (insufficient) and 1.0 (fully sufficient).</returns>
    Task<double> EvaluateAsync(
        string subQuery,
        IReadOnlyList<RetrievalResult> results,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Create IIterativeRetriever**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IIterativeRetriever.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Orchestrates multi-hop iterative retrieval for complex queries. Decomposes
/// the query, retrieves per sub-query in dependency order, evaluates sufficiency,
/// refines insufficient sub-queries, and aggregates results across hops.
/// </summary>
/// <remarks>
/// <para><strong>Pipeline flow:</strong></para>
/// <list type="number">
///   <item>Decompose query via <see cref="IQueryDecomposer"/>.</item>
///   <item>For each sub-query (in dependency order):
///     <list type="bullet">
///       <item>Retrieve via <see cref="IHybridRetriever"/>.</item>
///       <item>Evaluate sufficiency via <see cref="ISufficiencyEvaluator"/>.</item>
///       <item>If insufficient, refine the sub-query with prior context and re-retrieve.</item>
///     </list>
///   </item>
///   <item>Aggregate and deduplicate results across all hops.</item>
///   <item>Enforce token budget --- stop early if budget exhausted.</item>
///   <item>Hard cap at <c>MaxHops</c> total iterations.</item>
/// </list>
/// </remarks>
public interface IIterativeRetriever
{
    /// <summary>
    /// Execute iterative multi-hop retrieval for a complex query.
    /// </summary>
    /// <param name="query">The original complex query.</param>
    /// <param name="topKPerHop">Number of results to retrieve per hop.</param>
    /// <param name="collectionName">Optional collection name for scoped retrieval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IterativeRetrievalResult"/> with all hops, aggregated results,
    /// and budget status.
    /// </returns>
    Task<IterativeRetrievalResult> RetrieveIterativelyAsync(
        string query,
        int topKPerHop,
        string? collectionName = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create IAnswerFaithfulnessEvaluator**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IAnswerFaithfulnessEvaluator.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Evaluates whether an assembled answer is faithful to the retrieved context.
/// Identifies hallucinated claims not grounded in the source material and
/// supported claims that are properly backed by evidence.
/// </summary>
/// <remarks>
/// <para><strong>Implementation guidance:</strong></para>
/// <list type="bullet">
///   <item>Use LLM prompting to decompose the answer into individual claims.</item>
///   <item>Check each claim against the supporting context for grounding.</item>
///   <item>On LLM failure, return a conservative unfaithful evaluation (fail-safe).</item>
///   <item>Route to <c>"faithfulness_evaluation"</c> operation via <see cref="IRagModelRouter"/>.</item>
/// </list>
/// </remarks>
public interface IAnswerFaithfulnessEvaluator
{
    /// <summary>
    /// Evaluate whether the answer is faithful to the supporting context.
    /// </summary>
    /// <param name="answer">The assembled answer text to evaluate.</param>
    /// <param name="supportingContext">The reranked results that the answer should be grounded in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="FaithfulnessEvaluation"/> with the faithfulness score,
    /// hallucinated claims, and supported claims.
    /// </returns>
    Task<FaithfulnessEvaluation> EvaluateAsync(
        string answer,
        IReadOnlyList<RerankedResult> supportingContext,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryDecomposer.cs src/Content/Application/Application.AI.Common/Interfaces/RAG/ISufficiencyEvaluator.cs src/Content/Application/Application.AI.Common/Interfaces/RAG/IIterativeRetriever.cs src/Content/Application/Application.AI.Common/Interfaces/RAG/IAnswerFaithfulnessEvaluator.cs
git commit -m "feat(rag): add multi-hop and faithfulness evaluation interfaces"
```

---

### Task 4: Test Data Builders --- Extend RagTestData

**Files:**
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs`

- [ ] **Step 1: Add multi-hop test data builders to RagTestData**

Add these methods to the existing `RagTestData` class:

```csharp
// Add to RagTestData.cs — after the existing CreateConfigMonitor method

public static SubQuery CreateSubQuery(
    string text = "What is the default chunking strategy?",
    int order = 1,
    IReadOnlyList<int>? dependsOnOrders = null) =>
    new()
    {
        Text = text,
        Order = order,
        DependsOnOrders = dependsOnOrders ?? []
    };

public static DecomposedQuery CreateDecomposedQuery(
    string originalQuery = "Complex multi-part query",
    params string[] subQueryTexts)
{
    var texts = subQueryTexts.Length > 0
        ? subQueryTexts
        : new[] { "Sub-query 1: first part", "Sub-query 2: second part" };

    var subQueries = texts.Select((text, i) => new SubQuery
    {
        Text = text,
        Order = i + 1,
        DependsOnOrders = i > 0 ? [i] : []
    }).ToList();

    return new DecomposedQuery
    {
        OriginalQuery = originalQuery,
        SubQueries = subQueries
    };
}

public static HopResult CreateHopResult(
    SubQuery? subQuery = null,
    IReadOnlyList<RetrievalResult>? results = null,
    double sufficiencyScore = 0.8,
    int hopNumber = 1,
    bool? isSufficient = null) =>
    new()
    {
        SubQuery = subQuery ?? CreateSubQuery(),
        Results = results ?? CreateRetrievalResults(3),
        SufficiencyScore = sufficiencyScore,
        HopNumber = hopNumber,
        IsSufficient = isSufficient ?? sufficiencyScore >= 0.7
    };

public static IterativeRetrievalResult CreateIterativeRetrievalResult(
    IReadOnlyList<HopResult>? hops = null,
    int totalTokensUsed = 512,
    bool budgetExhausted = false)
{
    var effectiveHops = hops ?? [CreateHopResult()];
    var aggregated = effectiveHops
        .SelectMany(h => h.Results)
        .GroupBy(r => r.Chunk.Id)
        .Select(g => g.OrderByDescending(r => r.FusedScore).First())
        .ToList();

    return new IterativeRetrievalResult
    {
        Hops = effectiveHops,
        AggregatedResults = aggregated,
        TotalTokensUsed = totalTokensUsed,
        BudgetExhausted = budgetExhausted
    };
}

public static FaithfulnessEvaluation CreateFaithfulEvaluation(double score = 0.9) =>
    new()
    {
        IsFaithful = true,
        Score = score,
        SupportedClaims = ["Claim A is supported by chunk-1", "Claim B is supported by chunk-2"],
        HallucinatedClaims = [],
        Reasoning = "All claims are grounded in the retrieved context."
    };

public static FaithfulnessEvaluation CreateUnfaithfulEvaluation(
    IReadOnlyList<string>? hallucinatedClaims = null) =>
    new()
    {
        IsFaithful = false,
        Score = 0.3,
        SupportedClaims = ["Claim A is supported by chunk-1"],
        HallucinatedClaims = hallucinatedClaims ?? ["Claim X has no source", "Claim Y contradicts chunk-2"],
        Reasoning = "Multiple claims are not grounded in the retrieved context."
    };

public static MultiHopConfig CreateMultiHopConfig(Action<MultiHopConfig>? configure = null)
{
    var config = new MultiHopConfig
    {
        Enabled = true,
        MaxHops = 3,
        TokenBudgetPerHop = 1024,
        MinSufficiencyScore = 0.7,
        TopKPerHop = 5,
    };
    configure?.Invoke(config);
    return config;
}

public static FaithfulnessConfig CreateFaithfulnessConfig(Action<FaithfulnessConfig>? configure = null)
{
    var config = new FaithfulnessConfig
    {
        Enabled = true,
        HallucinationThreshold = 0.3,
        RequireCitationSupport = true,
    };
    configure?.Invoke(config);
    return config;
}
```

- [ ] **Step 2: Update CreateConfigMonitor to include MultiHop and Faithfulness**

In the existing `CreateConfigMonitor` method, add after the existing `QueryTransform` config:

```csharp
appConfig.AI.Rag.MultiHop = new MultiHopConfig
{
    Enabled = true,
    MaxHops = 3,
    TokenBudgetPerHop = 1024,
    MinSufficiencyScore = 0.7,
    TopKPerHop = 5,
};
appConfig.AI.Rag.Faithfulness = new FaithfulnessConfig
{
    Enabled = true,
    HallucinationThreshold = 0.3,
    RequireCitationSupport = true,
};
```

- [ ] **Step 3: Add required using directives**

Add at the top of `RagTestData.cs`:

```csharp
using Domain.Common.Config.AI.RAG;
```

- [ ] **Step 4: Build and run existing tests to verify no regression**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --verbosity normal`
Expected: All existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs
git commit -m "test(rag): add multi-hop and faithfulness test data builders"
```

---

### Task 5: Implementation --- QueryDecomposer

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryDecomposerTests.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryDecomposer.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryDecomposerTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.QueryTransform;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.QueryTransform;

public sealed class QueryDecomposerTests
{
    private readonly Mock<IRagModelRouter> _mockRouter = new();
    private readonly Mock<IChatClient> _mockChatClient = new();

    public QueryDecomposerTests()
    {
        _mockRouter
            .Setup(r => r.GetClientForOperation("query_decomposition"))
            .Returns(_mockChatClient.Object);
    }

    private QueryDecomposer CreateDecomposer()
        => new(
            _mockRouter.Object,
            Mock.Of<ILogger<QueryDecomposer>>());

    private void SetupChatResponse(string jsonResponse)
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, jsonResponse)));
    }

    [Fact]
    public async Task DecomposeAsync_ComplexMultiPartQuery_ReturnsOrderedSubQueries()
    {
        SetupChatResponse("""
            {
                "sub_queries": [
                    {"text": "What chunking strategies are available?", "order": 1, "depends_on": []},
                    {"text": "How does RAPTOR summarization work?", "order": 2, "depends_on": []},
                    {"text": "How do chunking strategies interact with RAPTOR?", "order": 3, "depends_on": [1, 2]}
                ]
            }
            """);
        var decomposer = CreateDecomposer();

        var result = await decomposer.DecomposeAsync(
            "How do the chunking strategies interact with RAPTOR summarization in the ingestion pipeline?");

        result.SubQueries.Should().HaveCount(3);
        result.SubQueries[0].Order.Should().Be(1);
        result.SubQueries[1].Order.Should().Be(2);
        result.SubQueries[2].Order.Should().Be(3);
        result.OriginalQuery.Should().Contain("chunking strategies");
    }

    [Fact]
    public async Task DecomposeAsync_SimpleQuery_ReturnsSingleSubQuery()
    {
        SetupChatResponse("""
            {
                "sub_queries": [
                    {"text": "What is the default topK value?", "order": 1, "depends_on": []}
                ]
            }
            """);
        var decomposer = CreateDecomposer();

        var result = await decomposer.DecomposeAsync("What is the default topK value?");

        result.SubQueries.Should().HaveCount(1);
        result.SubQueries[0].Text.Should().Be("What is the default topK value?");
        result.SubQueries[0].DependsOnOrders.Should().BeEmpty();
        result.RequiresSequentialExecution.Should().BeFalse();
    }

    [Fact]
    public async Task DecomposeAsync_QueryWithDependencies_SetsDependsOnOrders()
    {
        SetupChatResponse("""
            {
                "sub_queries": [
                    {"text": "What is the architecture?", "order": 1, "depends_on": []},
                    {"text": "What are the deployment requirements?", "order": 2, "depends_on": [1]}
                ]
            }
            """);
        var decomposer = CreateDecomposer();

        var result = await decomposer.DecomposeAsync(
            "Based on the architecture, what are the deployment requirements?");

        result.SubQueries[1].DependsOnOrders.Should().Contain(1);
    }

    [Fact]
    public async Task DecomposeAsync_SetsRequiresSequentialExecution_WhenDependenciesExist()
    {
        SetupChatResponse("""
            {
                "sub_queries": [
                    {"text": "Part A", "order": 1, "depends_on": []},
                    {"text": "Part B depends on A", "order": 2, "depends_on": [1]}
                ]
            }
            """);
        var decomposer = CreateDecomposer();

        var result = await decomposer.DecomposeAsync("Query with dependencies");

        result.RequiresSequentialExecution.Should().BeTrue();
    }

    [Fact]
    public async Task DecomposeAsync_EmptyQuery_ThrowsArgumentException()
    {
        var decomposer = CreateDecomposer();

        var act = () => decomposer.DecomposeAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DecomposeAsync_LlmReturnsInvalidJson_ReturnsSingleSubQueryFallback()
    {
        SetupChatResponse("I cannot decompose this query into structured JSON.");
        var decomposer = CreateDecomposer();

        var result = await decomposer.DecomposeAsync("Some complex query");

        result.SubQueries.Should().HaveCount(1);
        result.SubQueries[0].Text.Should().Be("Some complex query");
        result.SubQueries[0].Order.Should().Be(1);
        result.SubQueries[0].DependsOnOrders.Should().BeEmpty();
        result.RequiresSequentialExecution.Should().BeFalse();
    }

    [Fact]
    public async Task DecomposeAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var decomposer = CreateDecomposer();

        var act = () => decomposer.DecomposeAsync("test query", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "QueryDecomposerTests" --verbosity normal`
Expected: FAIL --- `QueryDecomposer` class does not exist.

- [ ] **Step 3: Implement QueryDecomposer**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryDecomposer.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.QueryTransform;

/// <summary>
/// LLM-based query decomposer that breaks complex multi-part queries into ordered
/// sub-queries with dependency tracking. Uses few-shot prompting to produce structured
/// decompositions. Falls back to a single sub-query wrapping the original on any failure.
/// </summary>
/// <remarks>
/// <para>
/// The decomposer identifies independent sub-questions that can be answered via separate
/// retrieval passes, and establishes dependency ordering when a sub-question requires
/// context from a prior answer. This enables the <see cref="IIterativeRetriever"/> to
/// execute sub-queries in the correct order and inject prior hop results as context.
/// </para>
/// </remarks>
public sealed class QueryDecomposer : IQueryDecomposer
{
    private readonly IRagModelRouter _modelRouter;
    private readonly ILogger<QueryDecomposer> _logger;

    private const string SystemPrompt = """
        You are a query decomposition engine for a RAG (Retrieval-Augmented Generation) system.
        Your job is to break complex, multi-part questions into smaller, independently-retrievable sub-queries.

        **Rules:**
        1. Each sub-query should target a single concept or fact that can be answered by one retrieval pass.
        2. Assign a 1-based sequential order to each sub-query.
        3. If a sub-query depends on the answer to a prior sub-query, set "depends_on" to an array of those order numbers.
        4. If the query is already simple and cannot be decomposed, return a single sub-query with the original text.
        5. Keep sub-query text concise but self-contained (understandable without seeing other sub-queries).

        **Examples:**

        User: "What chunking strategies are available and how do they compare for code files?"
        Response:
        {
            "sub_queries": [
                {"text": "What chunking strategies are available in the RAG pipeline?", "order": 1, "depends_on": []},
                {"text": "How do the chunking strategies compare for code files?", "order": 2, "depends_on": [1]}
            ]
        }

        User: "Based on the architecture docs and the deployment guide, what changes are needed to support multi-tenant GraphRAG?"
        Response:
        {
            "sub_queries": [
                {"text": "What is the current GraphRAG architecture?", "order": 1, "depends_on": []},
                {"text": "What does the deployment guide specify about multi-tenancy?", "order": 2, "depends_on": []},
                {"text": "What changes are needed to support multi-tenant GraphRAG given the architecture and deployment constraints?", "order": 3, "depends_on": [1, 2]}
            ]
        }

        User: "What is the default topK value?"
        Response:
        {
            "sub_queries": [
                {"text": "What is the default topK value?", "order": 1, "depends_on": []}
            ]
        }

        Respond with JSON only. No explanation, no markdown fences.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryDecomposer"/> class.
    /// </summary>
    /// <param name="modelRouter">Model router for resolving the LLM client.</param>
    /// <param name="logger">Logger for decomposition diagnostics.</param>
    public QueryDecomposer(
        IRagModelRouter modelRouter,
        ILogger<QueryDecomposer> logger)
    {
        _modelRouter = modelRouter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DecomposedQuery> DecomposeAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var client = _modelRouter.GetClientForOperation("query_decomposition");
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, query),
            };

            var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var content = response.Text?.Trim() ?? string.Empty;

            return ParseDecomposition(query, content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query decomposition failed, falling back to single sub-query");
            return CreateFallback(query);
        }
    }

    private DecomposedQuery ParseDecomposition(string originalQuery, string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                return CreateFallback(originalQuery);

            var json = content[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("sub_queries", out var subQueriesElement)
                || subQueriesElement.ValueKind != JsonValueKind.Array)
                return CreateFallback(originalQuery);

            var subQueries = new List<SubQuery>();
            foreach (var item in subQueriesElement.EnumerateArray())
            {
                var text = item.GetProperty("text").GetString() ?? string.Empty;
                var order = item.TryGetProperty("order", out var orderProp) ? orderProp.GetInt32() : subQueries.Count + 1;

                var dependsOn = new List<int>();
                if (item.TryGetProperty("depends_on", out var depsProp) && depsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in depsProp.EnumerateArray())
                    {
                        dependsOn.Add(dep.GetInt32());
                    }
                }

                subQueries.Add(new SubQuery
                {
                    Text = text,
                    Order = order,
                    DependsOnOrders = dependsOn,
                });
            }

            if (subQueries.Count == 0)
                return CreateFallback(originalQuery);

            // Sort by order to ensure correct sequence
            subQueries.Sort((a, b) => a.Order.CompareTo(b.Order));

            _logger.LogDebug(
                "Decomposed query into {Count} sub-queries, sequential={Sequential}",
                subQueries.Count,
                subQueries.Any(sq => sq.DependsOnOrders.Count > 0));

            return new DecomposedQuery
            {
                OriginalQuery = originalQuery,
                SubQueries = subQueries,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse decomposition JSON, falling back to single sub-query");
            return CreateFallback(originalQuery);
        }
    }

    private static DecomposedQuery CreateFallback(string originalQuery) =>
        new()
        {
            OriginalQuery = originalQuery,
            SubQueries =
            [
                new SubQuery
                {
                    Text = originalQuery,
                    Order = 1,
                    DependsOnOrders = [],
                }
            ],
        };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "QueryDecomposerTests" --verbosity normal`
Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryDecomposer.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryDecomposerTests.cs
git commit -m "feat(rag): implement QueryDecomposer with LLM few-shot decomposition"
```

---

### Task 6: Implementation --- SufficiencyEvaluator

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/SufficiencyEvaluatorTests.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/SufficiencyEvaluator.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/SufficiencyEvaluatorTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.Evaluation;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Evaluation;

public sealed class SufficiencyEvaluatorTests
{
    private readonly Mock<IRagModelRouter> _mockRouter = new();
    private readonly Mock<IChatClient> _mockChatClient = new();

    public SufficiencyEvaluatorTests()
    {
        _mockRouter
            .Setup(r => r.GetClientForOperation("sufficiency_evaluation"))
            .Returns(_mockChatClient.Object);
    }

    private SufficiencyEvaluator CreateEvaluator()
        => new(
            _mockRouter.Object,
            Mock.Of<ILogger<SufficiencyEvaluator>>());

    private void SetupChatResponse(string response)
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, response)));
    }

    [Fact]
    public async Task EvaluateAsync_SufficientContext_ReturnsHighScore()
    {
        SetupChatResponse("""{"score": 0.92, "reasoning": "Context fully addresses the question"}""");
        var evaluator = CreateEvaluator();
        var results = RagTestData.CreateRetrievalResults(3);

        var score = await evaluator.EvaluateAsync("What is the default topK?", results);

        score.Should().BeGreaterThanOrEqualTo(0.9);
    }

    [Fact]
    public async Task EvaluateAsync_InsufficientContext_ReturnsLowScore()
    {
        SetupChatResponse("""{"score": 0.25, "reasoning": "Context does not address the question"}""");
        var evaluator = CreateEvaluator();
        var results = RagTestData.CreateRetrievalResults(2);

        var score = await evaluator.EvaluateAsync("Unrelated sub-query", results);

        score.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyResults_ReturnsZero()
    {
        var evaluator = CreateEvaluator();
        IReadOnlyList<RetrievalResult> emptyResults = [];

        var score = await evaluator.EvaluateAsync("Any sub-query", emptyResults);

        score.Should().Be(0.0);
        _mockChatClient.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_LlmReturnsInvalidResponse_ReturnsDefaultScore()
    {
        SetupChatResponse("I'm not sure how to evaluate this.");
        var evaluator = CreateEvaluator();
        var results = RagTestData.CreateRetrievalResults(3);

        var score = await evaluator.EvaluateAsync("Some sub-query", results);

        score.Should().Be(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var evaluator = CreateEvaluator();
        var results = RagTestData.CreateRetrievalResults(3);

        var act = () => evaluator.EvaluateAsync("test", results, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "SufficiencyEvaluatorTests" --verbosity normal`
Expected: FAIL --- `SufficiencyEvaluator` class does not exist.

- [ ] **Step 3: Implement SufficiencyEvaluator**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/SufficiencyEvaluator.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.Evaluation;

/// <summary>
/// LLM-based evaluator that assesses whether retrieved chunks sufficiently answer
/// a sub-query. Returns a score between 0.0 (completely insufficient) and 1.0
/// (fully sufficient). Returns 0.0 immediately for empty result sets without
/// calling the LLM. Falls back to 0.5 (uncertain) on any LLM failure.
/// </summary>
public sealed class SufficiencyEvaluator : ISufficiencyEvaluator
{
    private readonly IRagModelRouter _modelRouter;
    private readonly ILogger<SufficiencyEvaluator> _logger;

    private const double DefaultScore = 0.5;

    private const string SystemPrompt = """
        You are a retrieval sufficiency evaluator for a RAG system. Given a sub-query and a set of
        retrieved document chunks, assess whether the chunks contain enough information to fully
        answer the sub-query.

        **Scoring guide:**
        - **0.9-1.0**: Chunks directly and completely answer the sub-query with specific details.
        - **0.7-0.89**: Chunks address the main question but may lack minor details.
        - **0.5-0.69**: Chunks are partially relevant but miss key aspects of the question.
        - **0.3-0.49**: Chunks are tangentially related but do not meaningfully answer the question.
        - **0.0-0.29**: Chunks are irrelevant to the sub-query.

        Respond with JSON only: {"score": 0.0-1.0, "reasoning": "brief explanation"}
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SufficiencyEvaluator"/> class.
    /// </summary>
    /// <param name="modelRouter">Model router for resolving the LLM client.</param>
    /// <param name="logger">Logger for evaluation diagnostics.</param>
    public SufficiencyEvaluator(
        IRagModelRouter modelRouter,
        ILogger<SufficiencyEvaluator> logger)
    {
        _modelRouter = modelRouter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<double> EvaluateAsync(
        string subQuery,
        IReadOnlyList<RetrievalResult> results,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (results.Count == 0)
        {
            _logger.LogDebug("No results to evaluate sufficiency for sub-query; returning 0.0");
            return 0.0;
        }

        try
        {
            var client = _modelRouter.GetClientForOperation("sufficiency_evaluation");

            var chunksText = string.Join("\n---\n", results.Select((r, i) =>
                $"[Chunk {i + 1}] (score: {r.FusedScore:F2})\n{r.Chunk.Content}"));

            var userPrompt = $"""
                **Sub-query:** {subQuery}

                **Retrieved chunks:**
                {chunksText}

                Evaluate whether these chunks sufficiently answer the sub-query.
                """;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, userPrompt),
            };

            var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var content = response.Text?.Trim() ?? string.Empty;

            return ParseScore(content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sufficiency evaluation failed, returning default score {Score}", DefaultScore);
            return DefaultScore;
        }
    }

    private double ParseScore(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                return DefaultScore;

            var json = content[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("score", out var scoreProp))
            {
                var score = scoreProp.GetDouble();
                return Math.Clamp(score, 0.0, 1.0);
            }

            return DefaultScore;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse sufficiency score, returning default");
            return DefaultScore;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "SufficiencyEvaluatorTests" --verbosity normal`
Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/SufficiencyEvaluator.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/SufficiencyEvaluatorTests.cs
git commit -m "feat(rag): implement SufficiencyEvaluator with LLM-based context scoring"
```

---

### Task 7: Implementation --- IterativeRetriever

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Retrieval/IterativeRetrieverTests.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/IterativeRetriever.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Retrieval/IterativeRetrieverTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.Retrieval;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Retrieval;

public sealed class IterativeRetrieverTests
{
    private readonly Mock<IQueryDecomposer> _mockDecomposer = new();
    private readonly Mock<IHybridRetriever> _mockRetriever = new();
    private readonly Mock<ISufficiencyEvaluator> _mockSufficiency = new();

    private IterativeRetriever CreateIterativeRetriever(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(configure);
        return new IterativeRetriever(
            _mockDecomposer.Object,
            _mockRetriever.Object,
            _mockSufficiency.Object,
            config,
            Mock.Of<ILogger<IterativeRetriever>>());
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_SimpleQuery_SingleHop()
    {
        // Single sub-query, sufficient on first hop
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "What is the default topK?",
            SubQueries =
            [
                new SubQuery { Text = "What is the default topK?", Order = 1 }
            ],
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        var retrievalResults = RagTestData.CreateRetrievalResults(3);
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrievalResults);

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.9);

        var retriever = CreateIterativeRetriever();

        var result = await retriever.RetrieveIterativelyAsync("What is the default topK?", topKPerHop: 5);

        result.Hops.Should().HaveCount(1);
        result.Hops[0].IsSufficient.Should().BeTrue();
        result.AggregatedResults.Should().HaveCount(3);
        result.BudgetExhausted.Should().BeFalse();
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_ComplexQuery_MultipleHops()
    {
        // Two independent sub-queries
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "Complex query",
            SubQueries =
            [
                new SubQuery { Text = "Part A", Order = 1 },
                new SubQuery { Text = "Part B", Order = 2 },
            ],
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        var resultsA = new List<RetrievalResult>
        {
            RagTestData.CreateRetrievalResult("chunk-a1", "Content A1", fusedScore: 0.9),
            RagTestData.CreateRetrievalResult("chunk-a2", "Content A2", fusedScore: 0.8),
        };
        var resultsB = new List<RetrievalResult>
        {
            RagTestData.CreateRetrievalResult("chunk-b1", "Content B1", fusedScore: 0.85),
        };

        var callCount = 0;
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? resultsA : resultsB;
            });

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.85);

        var retriever = CreateIterativeRetriever();

        var result = await retriever.RetrieveIterativelyAsync("Complex query", topKPerHop: 5);

        result.Hops.Should().HaveCount(2);
        result.AggregatedResults.Should().HaveCount(3); // a1, a2, b1
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_RespectsMaxHopsCap()
    {
        // 5 sub-queries but MaxHops = 3
        var subQueries = Enumerable.Range(1, 5)
            .Select(i => new SubQuery { Text = $"Part {i}", Order = i })
            .ToList();
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "Many parts",
            SubQueries = subQueries,
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateRetrievalResults(2));

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.85);

        var retriever = CreateIterativeRetriever(c => c.AI.Rag.MultiHop.MaxHops = 3);

        var result = await retriever.RetrieveIterativelyAsync("Many parts", topKPerHop: 5);

        result.Hops.Should().HaveCountLessOrEqualTo(3);
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_EnforceTokenBudget()
    {
        // Each chunk has ~10 tokens (content.Length / 4), budget = 30 tokens total
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "Budget test",
            SubQueries =
            [
                new SubQuery { Text = "Part 1", Order = 1 },
                new SubQuery { Text = "Part 2", Order = 2 },
                new SubQuery { Text = "Part 3", Order = 3 },
            ],
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        // Each result has content that uses ~12 tokens
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResult>
            {
                RagTestData.CreateRetrievalResult("chunk-big", "This is a long content string that should consume a meaningful portion of the token budget for testing purposes.", fusedScore: 0.9),
            });

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.85);

        var retriever = CreateIterativeRetriever(c => c.AI.Rag.MultiHop.TokenBudgetPerHop = 10);

        var result = await retriever.RetrieveIterativelyAsync("Budget test", topKPerHop: 5);

        result.BudgetExhausted.Should().BeTrue();
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_SubQueryDependencies_ExecutesInOrder()
    {
        // Sub-query 2 depends on sub-query 1
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "Dependent query",
            SubQueries =
            [
                new SubQuery { Text = "What is the architecture?", Order = 1 },
                new SubQuery { Text = "Based on the architecture, what needs changing?", Order = 2, DependsOnOrders = [1] },
            ],
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        var executionOrder = new List<string>();
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, string?, CancellationToken>((query, _, _, _) =>
            {
                executionOrder.Add(query);
                return Task.FromResult<IReadOnlyList<RetrievalResult>>(RagTestData.CreateRetrievalResults(2));
            });

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.85);

        var retriever = CreateIterativeRetriever();

        await retriever.RetrieveIterativelyAsync("Dependent query", topKPerHop: 5);

        executionOrder.Should().HaveCount(2);
        executionOrder[0].Should().Contain("architecture");
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_SufficientFirstHop_StopsEarly()
    {
        // Single sub-query, immediately sufficient
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "Simple",
            SubQueries =
            [
                new SubQuery { Text = "Simple question", Order = 1 },
            ],
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateRetrievalResults(3));

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.95);

        var retriever = CreateIterativeRetriever();

        var result = await retriever.RetrieveIterativelyAsync("Simple", topKPerHop: 5);

        result.Hops.Should().HaveCount(1);
        result.Hops[0].IsSufficient.Should().BeTrue();
        _mockRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_AggregatesResultsAcrossHops()
    {
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "Multi-hop",
            SubQueries =
            [
                new SubQuery { Text = "Part 1", Order = 1 },
                new SubQuery { Text = "Part 2", Order = 2 },
            ],
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        var callIndex = 0;
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callIndex++;
                return new List<RetrievalResult>
                {
                    RagTestData.CreateRetrievalResult($"chunk-hop{callIndex}-1", $"Content from hop {callIndex}", fusedScore: 0.9),
                    RagTestData.CreateRetrievalResult($"chunk-hop{callIndex}-2", $"More content from hop {callIndex}", fusedScore: 0.8),
                };
            });

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.85);

        var retriever = CreateIterativeRetriever();

        var result = await retriever.RetrieveIterativelyAsync("Multi-hop", topKPerHop: 5);

        result.AggregatedResults.Should().HaveCount(4); // 2 per hop, no overlap
    }

    [Fact]
    public async Task RetrieveIterativelyAsync_DeduplicatesChunksAcrossHops()
    {
        var decomposed = new DecomposedQuery
        {
            OriginalQuery = "Overlap query",
            SubQueries =
            [
                new SubQuery { Text = "Part 1", Order = 1 },
                new SubQuery { Text = "Part 2", Order = 2 },
            ],
        };
        _mockDecomposer
            .Setup(d => d.DecomposeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposed);

        // Both hops return the same chunk ID "chunk-shared" plus one unique each
        var sharedChunk = RagTestData.CreateRetrievalResult("chunk-shared", "Shared content", fusedScore: 0.9);
        var uniqueA = RagTestData.CreateRetrievalResult("chunk-a", "Unique A", fusedScore: 0.8);
        var uniqueB = RagTestData.CreateRetrievalResult("chunk-b", "Unique B", fusedScore: 0.7);

        var callCount = 0;
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new List<RetrievalResult> { sharedChunk, uniqueA }
                    : new List<RetrievalResult> { sharedChunk, uniqueB };
            });

        _mockSufficiency
            .Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.85);

        var retriever = CreateIterativeRetriever();

        var result = await retriever.RetrieveIterativelyAsync("Overlap query", topKPerHop: 5);

        result.AggregatedResults.Should().HaveCount(3); // shared, a, b (deduplicated)
        result.AggregatedResults.Select(r => r.Chunk.Id).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "IterativeRetrieverTests" --verbosity normal`
Expected: FAIL --- `IterativeRetriever` class does not exist.

- [ ] **Step 3: Implement IterativeRetriever**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/IterativeRetriever.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.Retrieval;

/// <summary>
/// Multi-hop iterative retriever that decomposes complex queries into sub-queries,
/// retrieves per sub-query in dependency order, evaluates sufficiency, refines
/// insufficient sub-queries, and aggregates results across hops. Enforces a hard
/// cap on iterations (<c>MaxHops</c>) and a per-hop token budget to prevent
/// context window overflow.
/// </summary>
/// <remarks>
/// <para>
/// The retrieval loop for each sub-query:
/// <list type="number">
///   <item>Retrieve via <see cref="IHybridRetriever"/> with configured <c>TopKPerHop</c>.</item>
///   <item>Evaluate sufficiency via <see cref="ISufficiencyEvaluator"/>.</item>
///   <item>If sufficient (score >= threshold), record the hop and move to the next sub-query.</item>
///   <item>If insufficient, refine the sub-query with prior context and re-retrieve (up to <c>MaxHops</c>).</item>
/// </list>
/// </para>
/// <para>
/// Dependencies are resolved by injecting the content from completed dependent sub-queries
/// into the refinement prompt, enabling later sub-queries to leverage prior hop results.
/// </para>
/// </remarks>
public sealed class IterativeRetriever : IIterativeRetriever
{
    private readonly IQueryDecomposer _decomposer;
    private readonly IHybridRetriever _hybridRetriever;
    private readonly ISufficiencyEvaluator _sufficiencyEvaluator;
    private readonly IOptionsMonitor<AppConfig> _configMonitor;
    private readonly ILogger<IterativeRetriever> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IterativeRetriever"/> class.
    /// </summary>
    /// <param name="decomposer">Query decomposer for splitting complex queries.</param>
    /// <param name="hybridRetriever">Hybrid retriever for per-hop retrieval.</param>
    /// <param name="sufficiencyEvaluator">Evaluator for assessing retrieval sufficiency.</param>
    /// <param name="configMonitor">Application configuration monitor.</param>
    /// <param name="logger">Logger for retrieval diagnostics.</param>
    public IterativeRetriever(
        IQueryDecomposer decomposer,
        IHybridRetriever hybridRetriever,
        ISufficiencyEvaluator sufficiencyEvaluator,
        IOptionsMonitor<AppConfig> configMonitor,
        ILogger<IterativeRetriever> logger)
    {
        _decomposer = decomposer;
        _hybridRetriever = hybridRetriever;
        _sufficiencyEvaluator = sufficiencyEvaluator;
        _configMonitor = configMonitor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IterativeRetrievalResult> RetrieveIterativelyAsync(
        string query,
        int topKPerHop,
        string? collectionName = null,
        CancellationToken cancellationToken = default)
    {
        var multiHopConfig = _configMonitor.CurrentValue.AI.Rag.MultiHop;
        var maxHops = multiHopConfig.MaxHops;
        var tokenBudgetPerHop = multiHopConfig.TokenBudgetPerHop;
        var minSufficiency = multiHopConfig.MinSufficiencyScore;
        var totalTokenBudget = maxHops * tokenBudgetPerHop;

        // Step 1: Decompose the query
        var decomposed = await _decomposer.DecomposeAsync(query, cancellationToken);
        _logger.LogInformation(
            "Decomposed query into {SubQueryCount} sub-queries, sequential={Sequential}",
            decomposed.SubQueries.Count, decomposed.RequiresSequentialExecution);

        var hops = new List<HopResult>();
        var allResults = new Dictionary<string, RetrievalResult>(); // keyed by chunk ID for dedup
        var totalTokensUsed = 0;
        var budgetExhausted = false;
        var hopNumber = 0;

        // Track completed sub-query results for dependency injection
        var completedSubQueryResults = new Dictionary<int, IReadOnlyList<RetrievalResult>>();

        // Process sub-queries in order
        foreach (var subQuery in decomposed.SubQueries.OrderBy(sq => sq.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (hopNumber >= maxHops)
            {
                _logger.LogInformation("Max hops ({MaxHops}) reached, stopping iteration", maxHops);
                break;
            }

            if (totalTokensUsed >= totalTokenBudget)
            {
                _logger.LogInformation("Token budget exhausted ({Used}/{Budget})", totalTokensUsed, totalTokenBudget);
                budgetExhausted = true;
                break;
            }

            // Build the effective query, enriching with dependent sub-query context
            var effectiveQuery = BuildEffectiveQuery(subQuery, completedSubQueryResults);

            hopNumber++;

            // Retrieve
            var candidates = await _hybridRetriever.RetrieveAsync(
                effectiveQuery, topKPerHop, collectionName, cancellationToken);

            // Calculate tokens used by this hop
            var hopTokens = candidates.Sum(r => r.Chunk.Tokens);
            totalTokensUsed += hopTokens;

            if (totalTokensUsed > totalTokenBudget)
            {
                budgetExhausted = true;
                _logger.LogDebug(
                    "Token budget exceeded on hop {Hop}: {Used}/{Budget}",
                    hopNumber, totalTokensUsed, totalTokenBudget);
            }

            // Evaluate sufficiency
            var sufficiencyScore = await _sufficiencyEvaluator.EvaluateAsync(
                subQuery.Text, candidates, cancellationToken);

            var isSufficient = sufficiencyScore >= minSufficiency;

            var hopResult = new HopResult
            {
                SubQuery = subQuery,
                Results = candidates,
                SufficiencyScore = sufficiencyScore,
                HopNumber = hopNumber,
                IsSufficient = isSufficient,
            };
            hops.Add(hopResult);

            // Add results to aggregation (dedup by chunk ID, keep highest score)
            foreach (var result in candidates)
            {
                if (allResults.TryGetValue(result.Chunk.Id, out var existing))
                {
                    if (result.FusedScore > existing.FusedScore)
                        allResults[result.Chunk.Id] = result;
                }
                else
                {
                    allResults[result.Chunk.Id] = result;
                }
            }

            // Track completed sub-query for dependency resolution
            completedSubQueryResults[subQuery.Order] = candidates;

            _logger.LogDebug(
                "Hop {Hop}: sub-query order={Order}, sufficiency={Score:F2}, sufficient={IsSufficient}, tokens={Tokens}",
                hopNumber, subQuery.Order, sufficiencyScore, isSufficient, hopTokens);
        }

        // Build aggregated results sorted by fused score descending
        var aggregatedResults = allResults.Values
            .OrderByDescending(r => r.FusedScore)
            .ToList();

        _logger.LogInformation(
            "Iterative retrieval complete: {Hops} hops, {Results} unique results, {Tokens} tokens, budgetExhausted={BudgetExhausted}",
            hops.Count, aggregatedResults.Count, totalTokensUsed, budgetExhausted);

        return new IterativeRetrievalResult
        {
            Hops = hops,
            AggregatedResults = aggregatedResults,
            TotalTokensUsed = totalTokensUsed,
            BudgetExhausted = budgetExhausted,
        };
    }

    private string BuildEffectiveQuery(
        SubQuery subQuery,
        Dictionary<int, IReadOnlyList<RetrievalResult>> completedResults)
    {
        if (subQuery.DependsOnOrders.Count == 0)
            return subQuery.Text;

        // Inject context from dependent sub-queries
        var contextParts = new List<string>();
        foreach (var depOrder in subQuery.DependsOnOrders)
        {
            if (completedResults.TryGetValue(depOrder, out var depResults) && depResults.Count > 0)
            {
                var contextSnippet = string.Join(" ", depResults.Select(r => r.Chunk.Content).Take(3));
                contextParts.Add($"[Context from step {depOrder}]: {contextSnippet}");
            }
        }

        if (contextParts.Count == 0)
            return subQuery.Text;

        var context = string.Join("\n", contextParts);
        return $"{subQuery.Text}\n\nPrior context:\n{context}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "IterativeRetrieverTests" --verbosity normal`
Expected: All 8 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/IterativeRetriever.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Retrieval/IterativeRetrieverTests.cs
git commit -m "feat(rag): implement IterativeRetriever with multi-hop decomposition and token budget"
```

---

### Task 8: Implementation --- AnswerFaithfulnessEvaluator

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/AnswerFaithfulnessEvaluatorTests.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/AnswerFaithfulnessEvaluator.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/AnswerFaithfulnessEvaluatorTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.Evaluation;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Evaluation;

public sealed class AnswerFaithfulnessEvaluatorTests
{
    private readonly Mock<IRagModelRouter> _mockRouter = new();
    private readonly Mock<IChatClient> _mockChatClient = new();

    public AnswerFaithfulnessEvaluatorTests()
    {
        _mockRouter
            .Setup(r => r.GetClientForOperation("faithfulness_evaluation"))
            .Returns(_mockChatClient.Object);
    }

    private AnswerFaithfulnessEvaluator CreateEvaluator()
        => new(
            _mockRouter.Object,
            Mock.Of<ILogger<AnswerFaithfulnessEvaluator>>());

    private void SetupChatResponse(string jsonResponse)
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, jsonResponse)));
    }

    [Fact]
    public async Task EvaluateAsync_FaithfulAnswer_ReturnsHighScore()
    {
        SetupChatResponse("""
            {
                "is_faithful": true,
                "score": 0.95,
                "supported_claims": ["The default topK is 10", "CRAG evaluation runs after reranking"],
                "hallucinated_claims": [],
                "reasoning": "All claims are directly supported by the retrieved context."
            }
            """);
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        var result = await evaluator.EvaluateAsync("The default topK is 10 and CRAG runs after reranking.", context);

        result.IsFaithful.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(0.9);
        result.SupportedClaims.Should().HaveCount(2);
        result.HallucinatedClaims.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_HallucinatedAnswer_ReturnsFlaggedClaims()
    {
        SetupChatResponse("""
            {
                "is_faithful": false,
                "score": 0.2,
                "supported_claims": ["The system uses hybrid retrieval"],
                "hallucinated_claims": ["The system uses GPT-5 for classification", "FAISS supports 10 billion vectors natively"],
                "reasoning": "Two claims have no support in the context."
            }
            """);
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        var result = await evaluator.EvaluateAsync("Hallucinated answer text", context);

        result.IsFaithful.Should().BeFalse();
        result.Score.Should().BeLessThan(0.5);
        result.HallucinatedClaims.Should().HaveCount(2);
        result.HallucinatedClaims.Should().Contain(c => c.Contains("GPT-5"));
    }

    [Fact]
    public async Task EvaluateAsync_PartiallyFaithful_ReturnsMiddleScore()
    {
        SetupChatResponse("""
            {
                "is_faithful": false,
                "score": 0.55,
                "supported_claims": ["The pipeline has 5 stages", "Reranking improves accuracy"],
                "hallucinated_claims": ["The pipeline uses quantum computing"],
                "reasoning": "Most claims are supported but one is fabricated."
            }
            """);
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        var result = await evaluator.EvaluateAsync("Partially faithful answer", context);

        result.Score.Should().BeInRange(0.4, 0.7);
        result.SupportedClaims.Should().NotBeEmpty();
        result.HallucinatedClaims.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_EmptyContext_ReturnsUnfaithful()
    {
        var evaluator = CreateEvaluator();
        IReadOnlyList<RerankedResult> emptyContext = [];

        var result = await evaluator.EvaluateAsync("Any answer", emptyContext);

        result.IsFaithful.Should().BeFalse();
        result.Score.Should().Be(0.0);
        _mockChatClient.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_LlmReturnsInvalidJson_ReturnsFallbackEvaluation()
    {
        SetupChatResponse("I cannot evaluate this answer.");
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        var result = await evaluator.EvaluateAsync("Some answer", context);

        result.IsFaithful.Should().BeFalse();
        result.Score.Should().Be(0.0);
        result.Reasoning.Should().Contain("failed");
    }

    [Fact]
    public async Task EvaluateAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var evaluator = CreateEvaluator();
        var context = RagTestData.CreateRerankedResults(3);

        var act = () => evaluator.EvaluateAsync("test", context, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "AnswerFaithfulnessEvaluatorTests" --verbosity normal`
Expected: FAIL --- `AnswerFaithfulnessEvaluator` class does not exist.

- [ ] **Step 3: Implement AnswerFaithfulnessEvaluator**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/AnswerFaithfulnessEvaluator.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.Evaluation;

/// <summary>
/// LLM-based evaluator that checks whether an assembled answer is faithful to the
/// retrieved context. Decomposes the answer into individual claims, verifies each
/// claim against the supporting context, and identifies hallucinated claims that
/// are not grounded in the source material.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator uses claim-level decomposition rather than holistic scoring to
/// provide actionable feedback: specific hallucinated claims can be removed or
/// flagged, enabling targeted corrective action rather than blanket re-retrieval.
/// </para>
/// <para>
/// On any LLM failure, the evaluator returns a conservative unfaithful result
/// (score 0.0) as a fail-safe, triggering corrective action. This is intentionally
/// cautious --- it is safer to refine a faithful answer than to pass a hallucinated one.
/// </para>
/// </remarks>
public sealed class AnswerFaithfulnessEvaluator : IAnswerFaithfulnessEvaluator
{
    private readonly IRagModelRouter _modelRouter;
    private readonly ILogger<AnswerFaithfulnessEvaluator> _logger;

    private const string SystemPrompt = """
        You are a faithfulness evaluator for a RAG (Retrieval-Augmented Generation) system.
        Given an answer and the retrieved context chunks it was generated from, evaluate whether
        the answer is faithful to the context.

        **Your task:**
        1. Decompose the answer into individual factual claims.
        2. For each claim, check if it is supported by the retrieved context.
        3. Classify each claim as "supported" or "hallucinated".
        4. Compute an overall faithfulness score (proportion of supported claims).

        **Scoring:**
        - 1.0: Every claim is directly supported by the context.
        - 0.7-0.99: Most claims supported, minor unsupported details.
        - 0.4-0.69: Mix of supported and unsupported claims.
        - 0.0-0.39: Most claims are not supported by the context.

        **Important:**
        - A claim is hallucinated if it states a specific fact not found in any context chunk.
        - Generic/obvious statements (e.g., "this is important") are not claims and should be ignored.
        - If a claim contradicts the context, it is hallucinated.
        - If a claim is a reasonable inference from the context, it is supported.

        Respond with JSON only:
        {
            "is_faithful": true/false,
            "score": 0.0-1.0,
            "supported_claims": ["claim 1", "claim 2"],
            "hallucinated_claims": ["claim X"],
            "reasoning": "brief explanation"
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnswerFaithfulnessEvaluator"/> class.
    /// </summary>
    /// <param name="modelRouter">Model router for resolving the LLM client.</param>
    /// <param name="logger">Logger for evaluation diagnostics.</param>
    public AnswerFaithfulnessEvaluator(
        IRagModelRouter modelRouter,
        ILogger<AnswerFaithfulnessEvaluator> logger)
    {
        _modelRouter = modelRouter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FaithfulnessEvaluation> EvaluateAsync(
        string answer,
        IReadOnlyList<RerankedResult> supportingContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (supportingContext.Count == 0)
        {
            _logger.LogDebug("No supporting context for faithfulness evaluation; returning unfaithful");
            return new FaithfulnessEvaluation
            {
                IsFaithful = false,
                Score = 0.0,
                HallucinatedClaims = [],
                SupportedClaims = [],
                Reasoning = "No supporting context available to verify faithfulness.",
            };
        }

        try
        {
            var client = _modelRouter.GetClientForOperation("faithfulness_evaluation");

            var contextText = string.Join("\n---\n", supportingContext.Select((r, i) =>
                $"[Chunk {i + 1}] (rerank score: {r.RerankScore:F2})\n{r.RetrievalResult.Chunk.Content}"));

            var userPrompt = $"""
                **Answer to evaluate:**
                {answer}

                **Retrieved context chunks:**
                {contextText}

                Evaluate whether the answer is faithful to the context.
                """;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, userPrompt),
            };

            var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var content = response.Text?.Trim() ?? string.Empty;

            return ParseEvaluation(content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Faithfulness evaluation failed, returning conservative unfaithful result");
            return CreateFallback();
        }
    }

    private FaithfulnessEvaluation ParseEvaluation(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                return CreateFallback();

            var json = content[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isFaithful = root.TryGetProperty("is_faithful", out var faithfulProp)
                && faithfulProp.GetBoolean();
            var score = root.TryGetProperty("score", out var scoreProp)
                ? Math.Clamp(scoreProp.GetDouble(), 0.0, 1.0)
                : 0.0;
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString()
                : null;

            var supportedClaims = ParseStringArray(root, "supported_claims");
            var hallucinatedClaims = ParseStringArray(root, "hallucinated_claims");

            return new FaithfulnessEvaluation
            {
                IsFaithful = isFaithful,
                Score = score,
                SupportedClaims = supportedClaims,
                HallucinatedClaims = hallucinatedClaims,
                Reasoning = reasoning,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse faithfulness evaluation JSON, returning fallback");
            return CreateFallback();
        }
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arrayProp)
            || arrayProp.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<string>();
        foreach (var element in arrayProp.EnumerateArray())
        {
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                items.Add(text);
        }
        return items;
    }

    private static FaithfulnessEvaluation CreateFallback() =>
        new()
        {
            IsFaithful = false,
            Score = 0.0,
            HallucinatedClaims = [],
            SupportedClaims = [],
            Reasoning = "Faithfulness evaluation failed; returning conservative unfaithful result.",
        };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "AnswerFaithfulnessEvaluatorTests" --verbosity normal`
Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Evaluation/AnswerFaithfulnessEvaluator.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Evaluation/AnswerFaithfulnessEvaluatorTests.cs
git commit -m "feat(rag): implement AnswerFaithfulnessEvaluator with claim-level hallucination detection"
```

---

### Task 9: Modify RagOrchestrator --- Multi-Hop Path for Complex Tier

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorMultiHopTests.cs`

- [ ] **Step 1: Write the new multi-hop orchestrator tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorMultiHopTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.Orchestration;
using Infrastructure.AI.RAG.QueryTransform;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

/// <summary>
/// Tests for the multi-hop retrieval path in RagOrchestrator.
/// Validates that Complex-tier queries use the IterativeRetriever and
/// that faithfulness evaluation triggers corrective action when needed.
/// </summary>
public sealed class RagOrchestratorMultiHopTests
{
    private readonly Mock<IHybridRetriever> _mockRetriever = new();
    private readonly Mock<IReranker> _mockReranker = new();
    private readonly Mock<ICragEvaluator> _mockCrag = new();
    private readonly Mock<IRagContextAssembler> _mockAssembler = new();
    private readonly Mock<IGraphRagService> _mockGraphRag = new();
    private readonly Mock<IQueryComplexityClassifier> _mockClassifier = new();
    private readonly Mock<IIterativeRetriever> _mockIterativeRetriever = new();
    private readonly Mock<IAnswerFaithfulnessEvaluator> _mockFaithfulness = new();

    private RagOrchestrator CreateOrchestrator(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(c =>
        {
            c.AI.Rag.ComplexityRouting.Enabled = true;
            c.AI.Rag.MultiHop.Enabled = true;
            c.AI.Rag.Faithfulness.Enabled = true;
            configure?.Invoke(c);
        });

        var gate = new RetrievalDecisionGate(config, Mock.Of<ILogger<RetrievalDecisionGate>>());
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
            config,
            Mock.Of<ILogger<RagOrchestrator>>(),
            _mockClassifier.Object,
            gate,
            _mockIterativeRetriever.Object,
            _mockFaithfulness.Object);
    }

    [Fact]
    public async Task SearchAsync_ComplexQuery_UsesIterativeRetriever()
    {
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateComplexClassification());

        var iterativeResult = RagTestData.CreateIterativeRetrievalResult();
        _mockIterativeRetriever
            .Setup(r => r.RetrieveIterativelyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iterativeResult);

        var rerankedResults = RagTestData.CreateRerankedResults(3);
        _mockReranker
            .Setup(r => r.RerankAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults);

        _mockAssembler
            .Setup(a => a.AssembleAsync(
                It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext
            {
                AssembledText = "Multi-hop assembled answer",
                TotalTokens = 200,
                WasTruncated = false,
            });

        _mockFaithfulness
            .Setup(f => f.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateFaithfulEvaluation());

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.SearchAsync(
            "Based on the architecture and deployment docs, what changes support multi-tenant GraphRAG?");

        result.AssembledText.Should().Be("Multi-hop assembled answer");
        _mockIterativeRetriever.Verify(
            r => r.RetrieveIterativelyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Complex queries should use IterativeRetriever, not HybridRetriever directly");
    }

    [Fact]
    public async Task SearchAsync_UnfaithfulAnswer_TriggersRefinement()
    {
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateComplexClassification());

        var iterativeResult = RagTestData.CreateIterativeRetrievalResult();
        _mockIterativeRetriever
            .Setup(r => r.RetrieveIterativelyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iterativeResult);

        var rerankedResults = RagTestData.CreateRerankedResults(3);
        _mockReranker
            .Setup(r => r.RerankAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults);

        var assembleCallCount = 0;
        _mockAssembler
            .Setup(a => a.AssembleAsync(
                It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                assembleCallCount++;
                return new RagAssembledContext
                {
                    AssembledText = assembleCallCount == 1 ? "Unfaithful first attempt" : "Refined faithful answer",
                    TotalTokens = 150,
                    WasTruncated = false,
                };
            });

        // First call returns unfaithful, triggering CRAG refinement
        _mockFaithfulness
            .Setup(f => f.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateUnfaithfulEvaluation());

        _mockCrag
            .Setup(c => c.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateAcceptEvaluation());

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.SearchAsync("Complex unfaithful query");

        // Faithfulness evaluator should have been called
        _mockFaithfulness.Verify(
            f => f.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // CRAG should have been triggered as corrective action
        _mockCrag.Verify(
            c => c.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_FaithfulAnswer_ReturnsDirectly()
    {
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateComplexClassification());

        var iterativeResult = RagTestData.CreateIterativeRetrievalResult();
        _mockIterativeRetriever
            .Setup(r => r.RetrieveIterativelyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iterativeResult);

        var rerankedResults = RagTestData.CreateRerankedResults(3);
        _mockReranker
            .Setup(r => r.RerankAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults);

        _mockAssembler
            .Setup(a => a.AssembleAsync(
                It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext
            {
                AssembledText = "Faithful multi-hop answer",
                TotalTokens = 180,
                WasTruncated = false,
            });

        _mockFaithfulness
            .Setup(f => f.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateFaithfulEvaluation());

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.SearchAsync("Complex faithful query");

        result.AssembledText.Should().Be("Faithful multi-hop answer");
        _mockCrag.Verify(
            c => c.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Faithful answer should not trigger CRAG refinement");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "RagOrchestratorMultiHopTests" --verbosity normal`
Expected: FAIL --- RagOrchestrator constructor does not accept `IIterativeRetriever` or `IAnswerFaithfulnessEvaluator`.

- [ ] **Step 3: Modify RagOrchestrator to accept multi-hop dependencies**

Update the constructor and add the multi-hop execution path. Modify `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs`:

Add fields:

```csharp
private readonly IQueryComplexityClassifier? _complexityClassifier;
private readonly IRetrievalDecisionGate? _decisionGate;
private readonly IIterativeRetriever? _iterativeRetriever;
private readonly IAnswerFaithfulnessEvaluator? _faithfulnessEvaluator;
```

Update the constructor signature (optional dependencies at the end):

```csharp
/// <summary>
/// Initializes a new instance of the <see cref="RagOrchestrator"/> class.
/// </summary>
/// <param name="hybridRetriever">Hybrid dense+sparse retriever.</param>
/// <param name="reranker">Cross-encoder or semantic reranker.</param>
/// <param name="cragEvaluator">CRAG relevance evaluator.</param>
/// <param name="contextAssembler">Final context assembly stage.</param>
/// <param name="graphRagService">Knowledge graph-based retrieval service.</param>
/// <param name="feedbackScorer">Optional feedback-weighted scorer. Null when feedback is disabled.</param>
/// <param name="queryRouter">Query classification and transformation router.</param>
/// <param name="configMonitor">Application configuration monitor.</param>
/// <param name="logger">Logger for recording pipeline decisions.</param>
/// <param name="complexityClassifier">Optional Phase A complexity classifier.</param>
/// <param name="decisionGate">Optional Phase A retrieval decision gate.</param>
/// <param name="iterativeRetriever">Optional Phase B multi-hop iterative retriever.</param>
/// <param name="faithfulnessEvaluator">Optional Phase B answer faithfulness evaluator.</param>
public RagOrchestrator(
    IHybridRetriever hybridRetriever,
    IReranker reranker,
    ICragEvaluator cragEvaluator,
    IRagContextAssembler contextAssembler,
    IGraphRagService graphRagService,
    IFeedbackWeightedScorer? feedbackScorer,
    QueryRouter queryRouter,
    IOptionsMonitor<AppConfig> configMonitor,
    ILogger<RagOrchestrator> logger,
    IQueryComplexityClassifier? complexityClassifier = null,
    IRetrievalDecisionGate? decisionGate = null,
    IIterativeRetriever? iterativeRetriever = null,
    IAnswerFaithfulnessEvaluator? faithfulnessEvaluator = null)
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
    _configMonitor = configMonitor;
    _logger = logger;
    _complexityClassifier = complexityClassifier;
    _decisionGate = decisionGate;
    _iterativeRetriever = iterativeRetriever;
    _faithfulnessEvaluator = faithfulnessEvaluator;
}
```

In `SearchAsync`, after the existing complexity routing block (Phase A), add the multi-hop branch within the `ExecuteRoutedPipelineAsync` method. Replace the `ExecuteRoutedPipelineAsync` with a version that handles Complex tier:

```csharp
private async Task<RagAssembledContext> ExecuteRoutedPipelineAsync(
    string query, RetrievalDecision decision, string? collectionName,
    CancellationToken cancellationToken)
{
    // Phase B: Complex tier uses multi-hop iterative retrieval
    var ragConfig = _configMonitor.CurrentValue.AI.Rag;
    if (decision.Complexity == QueryComplexity.Complex
        && ragConfig.MultiHop.Enabled
        && _iterativeRetriever is not null)
    {
        return await ExecuteMultiHopPipelineAsync(
            query, decision.TopK, collectionName, cancellationToken);
    }

    // Standard single-pass retrieval for non-Complex tiers
    var candidates = await _hybridRetriever.RetrieveAsync(
        query, decision.TopK, collectionName, cancellationToken);

    if (candidates.Count == 0)
        return CreateEmptyContext("No relevant documents found.");

    IReadOnlyList<RerankedResult> reranked;

    if (decision.UseReranking)
    {
        reranked = await _reranker.RerankAsync(
            query, candidates, decision.TopK, cancellationToken);
    }
    else
    {
        reranked = candidates.Select((r, i) => new RerankedResult
        {
            RetrievalResult = r,
            RerankScore = r.FusedScore,
            OriginalRank = i + 1,
            RerankRank = i + 1,
        }).ToList();
    }

    if (decision.UseCragEvaluation)
    {
        return await ExecuteWithCragLoopAsync(
            query, reranked, candidates, collectionName, cancellationToken);
    }

    return await _contextAssembler.AssembleAsync(reranked, DefaultMaxTokens, cancellationToken);
}
```

Add the new multi-hop pipeline method:

```csharp
private async Task<RagAssembledContext> ExecuteMultiHopPipelineAsync(
    string query, int topKPerHop, string? collectionName,
    CancellationToken cancellationToken)
{
    using var activity = ActivitySource.StartActivity("rag.orchestrator.multi_hop_pipeline");
    var ragConfig = _configMonitor.CurrentValue.AI.Rag;

    _logger.LogInformation("Entering multi-hop pipeline for complex query");

    // Step 1: Iterative retrieval
    var iterativeResult = await _iterativeRetriever!.RetrieveIterativelyAsync(
        query, topKPerHop, collectionName, cancellationToken);

    activity?.SetTag("rag.multi_hop.hop_count", iterativeResult.Hops.Count);
    activity?.SetTag("rag.multi_hop.total_tokens", iterativeResult.TotalTokensUsed);
    activity?.SetTag("rag.multi_hop.budget_exhausted", iterativeResult.BudgetExhausted);

    if (iterativeResult.AggregatedResults.Count == 0)
    {
        _logger.LogWarning("Multi-hop retrieval returned 0 results");
        return CreateEmptyContext("No relevant documents found after multi-hop retrieval.");
    }

    // Step 2: Rerank the aggregated results
    var reranked = await _reranker.RerankAsync(
        query, iterativeResult.AggregatedResults, topKPerHop, cancellationToken);

    // Step 3: Assemble context
    var assembled = await _contextAssembler.AssembleAsync(
        reranked, DefaultMaxTokens, cancellationToken);

    // Step 4: Faithfulness evaluation (if enabled)
    if (ragConfig.Faithfulness.Enabled && _faithfulnessEvaluator is not null)
    {
        var faithfulness = await _faithfulnessEvaluator.EvaluateAsync(
            assembled.AssembledText, reranked, cancellationToken);

        activity?.SetTag("rag.faithfulness.score", faithfulness.Score);
        activity?.SetTag("rag.faithfulness.is_faithful", faithfulness.IsFaithful);
        activity?.SetTag("rag.faithfulness.hallucinated_count", faithfulness.HallucinatedClaims.Count);

        if (!faithfulness.IsFaithful)
        {
            _logger.LogWarning(
                "Faithfulness evaluation failed: score={Score:F2}, hallucinated={Count} claims. Triggering CRAG refinement.",
                faithfulness.Score, faithfulness.HallucinatedClaims.Count);

            // Trigger one CRAG-style refinement using the aggregated results
            var cragEval = await _cragEvaluator.EvaluateAsync(
                query, iterativeResult.AggregatedResults, cancellationToken);

            if (cragEval.Action == CorrectionAction.Accept || cragEval.Action == CorrectionAction.Refine)
            {
                var filtered = FilterWeakChunks(reranked, cragEval.WeakChunkIds);
                return await _contextAssembler.AssembleAsync(
                    filtered, DefaultMaxTokens, cancellationToken);
            }

            // If CRAG rejects, return what we have with a warning
            _logger.LogWarning("CRAG also rejected after faithfulness failure; returning best available");
        }
        else
        {
            _logger.LogInformation(
                "Faithfulness check passed: score={Score:F2}, {Supported} supported claims",
                faithfulness.Score, faithfulness.SupportedClaims.Count);
        }
    }

    return assembled;
}
```

- [ ] **Step 4: Update the DI registration in AddRagOrchestration to pass new dependencies**

In `DependencyInjection.cs`, update `AddRagOrchestration`:

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
            sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
            sp.GetRequiredService<ILogger<RagOrchestrator>>(),
            sp.GetService<IQueryComplexityClassifier>(),
            sp.GetService<IRetrievalDecisionGate>(),
            sp.GetService<IIterativeRetriever>(),
            sp.GetService<IAnswerFaithfulnessEvaluator>()));
}
```

- [ ] **Step 5: Run all orchestrator tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "RagOrchestrator" --verbosity normal`
Expected: All tests PASS (new multi-hop + existing).

- [ ] **Step 6: Run full test suite for regression check**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --verbosity normal`
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorMultiHopTests.cs
git commit -m "feat(rag): integrate multi-hop retrieval and faithfulness evaluation into RagOrchestrator"
```

---

### Task 10: DI Registration --- Wire Multi-Hop and Faithfulness Services

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs`

- [ ] **Step 1: Add AddRagMultiHop and AddRagFaithfulness methods**

Add these methods to `DependencyInjection.cs`:

```csharp
/// <summary>
/// Registers Phase B multi-hop iterative retrieval services: query decomposer,
/// sufficiency evaluator, and iterative retriever.
/// </summary>
private static void AddRagMultiHop(IServiceCollection services, AppConfig appConfig)
{
    services.AddSingleton<IQueryDecomposer, QueryDecomposer>();
    services.AddSingleton<ISufficiencyEvaluator, SufficiencyEvaluator>();
    services.AddSingleton<IIterativeRetriever, IterativeRetriever>();
}

/// <summary>
/// Registers Phase B answer faithfulness evaluation services.
/// </summary>
private static void AddRagFaithfulness(IServiceCollection services, AppConfig appConfig)
{
    services.AddSingleton<IAnswerFaithfulnessEvaluator, AnswerFaithfulnessEvaluator>();
}
```

- [ ] **Step 2: Call the new methods from AddRagDependencies**

Update `AddRagDependencies` to include the new registrations:

```csharp
public static IServiceCollection AddRagDependencies(
    this IServiceCollection services,
    AppConfig appConfig)
{
    AddRagIngestion(services, appConfig);
    AddRagRetrieval(services, appConfig);
    AddRagQueryTransform(services, appConfig);
    AddRagEvaluation(services, appConfig);
    AddRagGraphRag(services, appConfig);
    AddRagMultiHop(services, appConfig);        // Phase B
    AddRagFaithfulness(services, appConfig);     // Phase B
    AddRagOrchestration(services, appConfig);

    return services;
}
```

- [ ] **Step 3: Add required using directives**

Add at the top of `DependencyInjection.cs`:

```csharp
using Infrastructure.AI.RAG.Retrieval;
```

(The `QueryTransform` and `Evaluation` namespaces should already be imported.)

- [ ] **Step 4: Build and run full test suite**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx --verbosity normal`
Expected: Build succeeds, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs
git commit -m "feat(rag): register multi-hop and faithfulness services in DI"
```

---

### Task 11: Integration Tests --- End-to-End Multi-Hop Path

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiHopIntegrationTests.cs`

- [ ] **Step 1: Write integration tests covering the full multi-hop flow**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiHopIntegrationTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.Orchestration;
using Infrastructure.AI.RAG.QueryTransform;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

/// <summary>
/// End-to-end integration tests for the multi-hop retrieval path.
/// Exercises the full pipeline: complexity classification -> iterative retrieval ->
/// reranking -> assembly -> faithfulness evaluation -> (optional) CRAG refinement.
/// Uses real RetrievalDecisionGate with mocked LLM-dependent components.
/// </summary>
public sealed class MultiHopIntegrationTests
{
    private readonly Mock<IHybridRetriever> _mockRetriever = new();
    private readonly Mock<IReranker> _mockReranker = new();
    private readonly Mock<ICragEvaluator> _mockCrag = new();
    private readonly Mock<IRagContextAssembler> _mockAssembler = new();
    private readonly Mock<IGraphRagService> _mockGraphRag = new();
    private readonly Mock<IQueryComplexityClassifier> _mockClassifier = new();
    private readonly Mock<IIterativeRetriever> _mockIterativeRetriever = new();
    private readonly Mock<IAnswerFaithfulnessEvaluator> _mockFaithfulness = new();

    private RagOrchestrator CreateOrchestrator(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(c =>
        {
            c.AI.Rag.ComplexityRouting.Enabled = true;
            c.AI.Rag.MultiHop.Enabled = true;
            c.AI.Rag.Faithfulness.Enabled = true;
            configure?.Invoke(c);
        });

        var gate = new RetrievalDecisionGate(config, Mock.Of<ILogger<RetrievalDecisionGate>>());
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
            config,
            Mock.Of<ILogger<RagOrchestrator>>(),
            _mockClassifier.Object,
            gate,
            _mockIterativeRetriever.Object,
            _mockFaithfulness.Object);
    }

    [Fact]
    public async Task ComplexQuery_FullMultiHopPipeline_ReturnsAssembledContext()
    {
        // Arrange: classify as Complex, multi-hop retrieval, rerank, assemble, faithful
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateComplexClassification(0.85));

        var iterativeResult = RagTestData.CreateIterativeRetrievalResult(
            hops:
            [
                RagTestData.CreateHopResult(
                    subQuery: RagTestData.CreateSubQuery("What is the architecture?", 1),
                    results: RagTestData.CreateRetrievalResults(3),
                    sufficiencyScore: 0.9,
                    hopNumber: 1,
                    isSufficient: true),
                RagTestData.CreateHopResult(
                    subQuery: RagTestData.CreateSubQuery("What needs to change for multi-tenancy?", 2, [1]),
                    results: RagTestData.CreateRetrievalResults(2),
                    sufficiencyScore: 0.8,
                    hopNumber: 2,
                    isSufficient: true),
            ],
            totalTokensUsed: 800);
        _mockIterativeRetriever
            .Setup(r => r.RetrieveIterativelyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iterativeResult);

        var rerankedResults = RagTestData.CreateRerankedResults(5);
        _mockReranker
            .Setup(r => r.RerankAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults);

        _mockAssembler
            .Setup(a => a.AssembleAsync(
                It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext
            {
                AssembledText = "Comprehensive multi-hop answer covering architecture and multi-tenancy changes.",
                TotalTokens = 350,
                WasTruncated = false,
            });

        _mockFaithfulness
            .Setup(f => f.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateFaithfulEvaluation(0.92));

        // Act
        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.SearchAsync(
            "Based on the architecture and deployment docs, what changes support multi-tenant GraphRAG?");

        // Assert
        result.AssembledText.Should().Contain("multi-hop answer");
        result.TotalTokens.Should().Be(350);
        result.WasTruncated.Should().BeFalse();

        // Verify pipeline flow
        _mockClassifier.Verify(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockIterativeRetriever.Verify(r => r.RetrieveIterativelyAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockReranker.Verify(r => r.RerankAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockAssembler.Verify(a => a.AssembleAsync(
            It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockFaithfulness.Verify(f => f.EvaluateAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComplexQuery_MultiHopDisabled_UsesStandardPipeline()
    {
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateComplexClassification());

        var retrievalResults = RagTestData.CreateRetrievalResults(5);
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrievalResults);

        var rerankedResults = RagTestData.CreateRerankedResults(5);
        _mockReranker
            .Setup(r => r.RerankAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults);

        _mockCrag
            .Setup(c => c.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateAcceptEvaluation());

        _mockAssembler
            .Setup(a => a.AssembleAsync(
                It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext
            {
                AssembledText = "Standard pipeline result",
                TotalTokens = 200,
                WasTruncated = false,
            });

        var orchestrator = CreateOrchestrator(c => c.AI.Rag.MultiHop.Enabled = false);
        var result = await orchestrator.SearchAsync("Complex query with multi-hop disabled");

        result.AssembledText.Should().Be("Standard pipeline result");
        _mockIterativeRetriever.Verify(
            r => r.RetrieveIterativelyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Multi-hop disabled should use standard HybridRetriever");
        _mockRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ComplexQuery_FaithfulnessDisabled_SkipsFaithfulnessCheck()
    {
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateComplexClassification());

        var iterativeResult = RagTestData.CreateIterativeRetrievalResult();
        _mockIterativeRetriever
            .Setup(r => r.RetrieveIterativelyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iterativeResult);

        _mockReranker
            .Setup(r => r.RerankAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateRerankedResults(3));

        _mockAssembler
            .Setup(a => a.AssembleAsync(
                It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext
            {
                AssembledText = "Answer without faithfulness check",
                TotalTokens = 100,
                WasTruncated = false,
            });

        var orchestrator = CreateOrchestrator(c => c.AI.Rag.Faithfulness.Enabled = false);
        var result = await orchestrator.SearchAsync("Complex query, no faithfulness");

        result.AssembledText.Should().Be("Answer without faithfulness check");
        _mockFaithfulness.Verify(
            f => f.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Faithfulness disabled should skip evaluation");
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "MultiHopIntegrationTests" --verbosity normal`
Expected: All 3 tests PASS.

- [ ] **Step 3: Run full solution test suite**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx --verbosity normal`
Expected: Build succeeds, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiHopIntegrationTests.cs
git commit -m "test(rag): add end-to-end multi-hop integration tests covering full pipeline"
```

---

### Task 12: OTel Metrics --- Multi-Hop and Faithfulness Observability

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs`

- [ ] **Step 1: Verify OTel tags are present in the multi-hop pipeline**

The `ExecuteMultiHopPipelineAsync` method added in Task 9 already includes Activity tags. Verify these tags are set:

```csharp
// Already present in ExecuteMultiHopPipelineAsync:
activity?.SetTag("rag.multi_hop.hop_count", iterativeResult.Hops.Count);
activity?.SetTag("rag.multi_hop.total_tokens", iterativeResult.TotalTokensUsed);
activity?.SetTag("rag.multi_hop.budget_exhausted", iterativeResult.BudgetExhausted);
activity?.SetTag("rag.faithfulness.score", faithfulness.Score);
activity?.SetTag("rag.faithfulness.is_faithful", faithfulness.IsFaithful);
activity?.SetTag("rag.faithfulness.hallucinated_count", faithfulness.HallucinatedClaims.Count);
```

- [ ] **Step 2: Add per-hop sufficiency scores as Activity events**

Add sufficiency score tracking within the `IterativeRetriever` class. In `src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/IterativeRetriever.cs`, add an `ActivitySource` and emit per-hop events:

```csharp
using System.Diagnostics;

// Add field at the top of the class:
private static readonly ActivitySource ActivitySource = new("Infrastructure.AI.RAG.Retrieval.IterativeRetriever");

// In RetrieveIterativelyAsync, wrap the method body with an activity:
public async Task<IterativeRetrievalResult> RetrieveIterativelyAsync(
    string query,
    int topKPerHop,
    string? collectionName = null,
    CancellationToken cancellationToken = default)
{
    using var activity = ActivitySource.StartActivity("rag.iterative_retriever.retrieve");
    // ... existing logic ...

    // After each hop (after creating hopResult), add:
    activity?.AddEvent(new ActivityEvent("hop_completed", tags: new ActivityTagsCollection
    {
        { "rag.hop.number", hopNumber },
        { "rag.hop.sub_query_order", subQuery.Order },
        { "rag.hop.sufficiency_score", sufficiencyScore },
        { "rag.hop.is_sufficient", isSufficient },
        { "rag.hop.result_count", candidates.Count },
        { "rag.hop.tokens", hopTokens },
    }));

    // At the end, before returning, tag final metrics:
    activity?.SetTag("rag.iterative.total_hops", hops.Count);
    activity?.SetTag("rag.iterative.total_results", aggregatedResults.Count);
    activity?.SetTag("rag.iterative.total_tokens", totalTokensUsed);
    activity?.SetTag("rag.iterative.budget_exhausted", budgetExhausted);
    activity?.SetTag("rag.iterative.sub_query_count", decomposed.SubQueries.Count);

    // ... return statement ...
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx --verbosity normal`
Expected: Build succeeds, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/IterativeRetriever.cs src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs
git commit -m "feat(rag): add OTel tracing for multi-hop retrieval and faithfulness evaluation"
```

---

## Verification Checklist

After all tasks are complete, run the full verification:

```bash
dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx --verbosity normal
```

Expected test count increase: **+29 tests** (7 decomposer + 5 sufficiency + 8 iterative retriever + 6 faithfulness + 3 orchestrator multi-hop)

### Summary of Deliverables

| Component | Type | Tests |
|---|---|---|
| SubQuery, DecomposedQuery, HopResult, IterativeRetrievalResult, FaithfulnessEvaluation | Domain records | (compile-only) |
| MultiHopConfig, FaithfulnessConfig | Config classes | (compile-only) |
| IQueryDecomposer, ISufficiencyEvaluator, IIterativeRetriever, IAnswerFaithfulnessEvaluator | Interfaces | (compile-only) |
| RagTestData builders | Test helpers | (used by all tests) |
| QueryDecomposer | Infrastructure | 7 tests |
| SufficiencyEvaluator | Infrastructure | 5 tests |
| IterativeRetriever | Infrastructure | 8 tests |
| AnswerFaithfulnessEvaluator | Infrastructure | 6 tests |
| RagOrchestrator multi-hop path | Infrastructure (modify) | 3 tests |
| DI Registration | Infrastructure (modify) | (verified by integration) |
| Integration tests | Test | 3 tests |
| OTel tracing | Infrastructure (modify) | (verified by build) |
