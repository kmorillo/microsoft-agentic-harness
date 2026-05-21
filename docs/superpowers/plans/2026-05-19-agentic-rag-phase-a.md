# Phase A: Adaptive Routing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a retrieval decision gate and cost-aware query routing so the agent can skip retrieval for trivial queries, use lightweight retrieval for simple ones, and reserve the full CRAG pipeline for complex questions.

**Architecture:** A `QueryComplexityClassifier` determines query complexity (Trivial/Simple/Moderate/Complex). A `RetrievalDecisionGate` uses that classification to decide whether retrieval is needed at all. The `RagOrchestrator` gains new execution paths per complexity tier. `DocumentSearchTool` gains optional parameters for agent-controlled retrieval depth.

**Tech Stack:** C# .NET 10, Microsoft.Extensions.AI (IChatClient), xUnit + Moq + FluentAssertions, keyed DI

**Estimated impact:** 30-50% cost reduction on agent runs, ~35% latency improvement on simple queries, better answer quality by avoiding context poisoning.

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `src/Content/Domain/Domain.AI/RAG/Enums/QueryComplexity.cs` | Complexity tier enum |
| Create | `src/Content/Domain/Domain.AI/RAG/Models/ComplexityClassification.cs` | Classification result model |
| Create | `src/Content/Domain/Domain.Common/Config/AI/RAG/ComplexityRoutingConfig.cs` | Config for thresholds and tier mapping |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryComplexityClassifier.cs` | Complexity classification interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalDecisionGate.cs` | Retrieval skip/proceed decision interface |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryComplexityClassifier.cs` | LLM-based complexity classification |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs` | Decision logic: skip, lightweight, full pipeline |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs` | Add complexity-routed execution paths |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryRouter.cs` | Integrate complexity classifier |
| Modify | `src/Content/Infrastructure/Infrastructure.AI/Tools/DocumentSearchTool.cs` | Add optional `top_k`, `strategy`, `complexity_hint` params |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs` | Register new services |
| Modify | `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs` | Add `ComplexityRouting` property |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryComplexityClassifierTests.cs` | Classifier tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalDecisionGateTests.cs` | Decision gate tests |
| Modify | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorTests.cs` | Add routing tests |
| Modify | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs` | Add complexity test data builders |

---

### Task 1: Domain Models — QueryComplexity Enum and ComplexityClassification

**Files:**
- Create: `src/Content/Domain/Domain.AI/RAG/Enums/QueryComplexity.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/ComplexityClassification.cs`

- [ ] **Step 1: Create the QueryComplexity enum**

```csharp
// src/Content/Domain/Domain.AI/RAG/Enums/QueryComplexity.cs
namespace Domain.AI.RAG.Enums;

/// <summary>
/// Query complexity tier that determines the retrieval strategy cost/depth tradeoff.
/// </summary>
public enum QueryComplexity
{
    /// <summary>The LLM can answer directly from parametric knowledge. No retrieval needed.</summary>
    Trivial,

    /// <summary>Single-pass vector search suffices. Skip reranking and CRAG.</summary>
    Simple,

    /// <summary>Full hybrid pipeline with reranking and CRAG evaluation.</summary>
    Moderate,

    /// <summary>Multi-hop iterative retrieval with query decomposition (Phase B).</summary>
    Complex
}
```

- [ ] **Step 2: Create the ComplexityClassification model**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/ComplexityClassification.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// Result of query complexity classification with confidence and reasoning.
/// </summary>
public sealed record ComplexityClassification
{
    public required QueryComplexity Complexity { get; init; }
    public required double Confidence { get; init; }
    public string? Reasoning { get; init; }

    /// <summary>Whether retrieval should be skipped entirely for this query.</summary>
    public bool SkipRetrieval => Complexity == QueryComplexity.Trivial;
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Domain/Domain.AI/RAG/Enums/QueryComplexity.cs src/Content/Domain/Domain.AI/RAG/Models/ComplexityClassification.cs
git commit -m "feat(rag): add QueryComplexity enum and ComplexityClassification model"
```

---

### Task 2: Configuration — ComplexityRoutingConfig

**Files:**
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/ComplexityRoutingConfig.cs`
- Modify: `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs`

- [ ] **Step 1: Create ComplexityRoutingConfig**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/ComplexityRoutingConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for complexity-based query routing.
/// Controls thresholds, tier mapping, and cost-awareness.
/// </summary>
public sealed class ComplexityRoutingConfig
{
    /// <summary>Enable complexity-based routing. When false, all queries use the full pipeline.</summary>
    public bool Enabled { get; set; }

    /// <summary>Confidence threshold below which the classifier falls back to Moderate.</summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>TopK for Simple tier (lightweight single-pass). Lower = faster + cheaper.</summary>
    public int SimpleTopK { get; set; } = 5;

    /// <summary>TopK for Moderate tier (full pipeline). Uses existing Retrieval.TopK if not set.</summary>
    public int? ModerateTopK { get; set; }

    /// <summary>TopK for Complex tier (multi-hop, Phase B). Higher for comprehensive retrieval.</summary>
    public int ComplexTopK { get; set; } = 15;

    /// <summary>Skip reranking for Simple tier queries to save latency and cost.</summary>
    public bool SkipRerankForSimple { get; set; } = true;

    /// <summary>Skip CRAG evaluation for Simple tier queries.</summary>
    public bool SkipCragForSimple { get; set; } = true;
}
```

- [ ] **Step 2: Add ComplexityRouting property to RagConfig**

In `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs`, add:

```csharp
/// <summary>Complexity-based query routing configuration.</summary>
public ComplexityRoutingConfig ComplexityRouting { get; set; } = new();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Domain/Domain.Common/Config/AI/RAG/ComplexityRoutingConfig.cs src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs
git commit -m "feat(rag): add ComplexityRoutingConfig for cost-aware query routing"
```

---

### Task 3: Application Interfaces — IQueryComplexityClassifier and IRetrievalDecisionGate

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryComplexityClassifier.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalDecisionGate.cs`

- [ ] **Step 1: Create IQueryComplexityClassifier**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryComplexityClassifier.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Classifies query complexity to determine the appropriate retrieval cost tier.
/// </summary>
public interface IQueryComplexityClassifier
{
    /// <summary>
    /// Classify the complexity of a user query.
    /// </summary>
    /// <param name="query">The user's natural language query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification result with complexity tier, confidence, and reasoning.</returns>
    Task<ComplexityClassification> ClassifyAsync(string query, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create IRetrievalDecisionGate**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalDecisionGate.cs
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Decides the retrieval execution path based on query complexity.
/// Returns the effective retrieval parameters for the classified tier.
/// </summary>
public interface IRetrievalDecisionGate
{
    /// <summary>
    /// Determine retrieval parameters based on complexity classification.
    /// </summary>
    /// <param name="classification">The complexity classification result.</param>
    /// <param name="requestedTopK">Optional topK override from the caller.</param>
    /// <returns>Effective retrieval parameters for this query.</returns>
    RetrievalDecision Decide(ComplexityClassification classification, int? requestedTopK = null);
}

/// <summary>
/// The retrieval execution decision — what pipeline path to take and with what parameters.
/// </summary>
public sealed record RetrievalDecision
{
    /// <summary>Whether to skip retrieval entirely and let the LLM answer from parametric knowledge.</summary>
    public required bool SkipRetrieval { get; init; }

    /// <summary>Effective topK for this query.</summary>
    public required int TopK { get; init; }

    /// <summary>Whether to run the reranker.</summary>
    public required bool UseReranking { get; init; }

    /// <summary>Whether to run CRAG evaluation.</summary>
    public required bool UseCragEvaluation { get; init; }

    /// <summary>The complexity tier that produced this decision.</summary>
    public required QueryComplexity Complexity { get; init; }

    /// <summary>Optional strategy override based on complexity.</summary>
    public RetrievalStrategy? StrategyOverride { get; init; }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/RAG/IQueryComplexityClassifier.cs src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalDecisionGate.cs
git commit -m "feat(rag): add IQueryComplexityClassifier and IRetrievalDecisionGate interfaces"
```

---

### Task 4: Test Data Builders — Extend RagTestData

**Files:**
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs`

- [ ] **Step 1: Add complexity classification builders to RagTestData**

Add these methods to the existing `RagTestData` class:

```csharp
// Add to RagTestData.cs

public static ComplexityClassification CreateTrivialClassification(double confidence = 0.9)
    => new()
    {
        Complexity = QueryComplexity.Trivial,
        Confidence = confidence,
        Reasoning = "Query can be answered from general knowledge without retrieval."
    };

public static ComplexityClassification CreateSimpleClassification(double confidence = 0.85)
    => new()
    {
        Complexity = QueryComplexity.Simple,
        Confidence = confidence,
        Reasoning = "Direct factual lookup requiring single-pass retrieval."
    };

public static ComplexityClassification CreateModerateClassification(double confidence = 0.8)
    => new()
    {
        Complexity = QueryComplexity.Moderate,
        Confidence = confidence,
        Reasoning = "Query requires hybrid retrieval with quality evaluation."
    };

public static ComplexityClassification CreateComplexClassification(double confidence = 0.75)
    => new()
    {
        Complexity = QueryComplexity.Complex,
        Confidence = confidence,
        Reasoning = "Multi-hop query requiring iterative retrieval across documents."
    };

public static ComplexityRoutingConfig CreateComplexityRoutingConfig(Action<ComplexityRoutingConfig>? configure = null)
{
    var config = new ComplexityRoutingConfig
    {
        Enabled = true,
        ConfidenceThreshold = 0.7,
        SimpleTopK = 5,
        ModerateTopK = null,
        ComplexTopK = 15,
        SkipRerankForSimple = true,
        SkipCragForSimple = true,
    };
    configure?.Invoke(config);
    return config;
}
```

- [ ] **Step 2: Update CreateConfigMonitor to include ComplexityRouting**

In the existing `CreateConfigMonitor` method, add after the existing config setup:

```csharp
appConfig.AI.Rag.ComplexityRouting = new ComplexityRoutingConfig
{
    Enabled = true,
    ConfidenceThreshold = 0.7,
    SimpleTopK = 5,
    ComplexTopK = 15,
    SkipRerankForSimple = true,
    SkipCragForSimple = true,
};
```

- [ ] **Step 3: Build and run existing tests to verify no regression**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --verbosity normal`
Expected: All existing tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs
git commit -m "test(rag): add complexity classification test data builders"
```

---

### Task 5: Implementation — QueryComplexityClassifier

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryComplexityClassifierTests.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryComplexityClassifier.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryComplexityClassifierTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using FluentAssertions;
using Infrastructure.AI.RAG.QueryTransform;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.QueryTransform;

public sealed class QueryComplexityClassifierTests
{
    private readonly Mock<IRagModelRouter> _mockRouter = new();
    private readonly Mock<IChatClient> _mockChatClient = new();

    public QueryComplexityClassifierTests()
    {
        _mockRouter
            .Setup(r => r.GetClientForOperation("complexity_classification"))
            .Returns(_mockChatClient.Object);
    }

    private QueryComplexityClassifier CreateClassifier()
        => new(
            _mockRouter.Object,
            Mock.Of<ILogger<QueryComplexityClassifier>>());

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
    public async Task ClassifyAsync_TrivialQuery_ReturnsTrivial()
    {
        SetupChatResponse("""{"complexity":"trivial","confidence":0.95,"reasoning":"General knowledge question"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync("What is the capital of France?");

        result.Complexity.Should().Be(QueryComplexity.Trivial);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.9);
        result.SkipRetrieval.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyAsync_SimpleQuery_ReturnsSimple()
    {
        SetupChatResponse("""{"complexity":"simple","confidence":0.85,"reasoning":"Direct factual lookup"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync("What chunking strategies are available?");

        result.Complexity.Should().Be(QueryComplexity.Simple);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.8);
        result.SkipRetrieval.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_ModerateQuery_ReturnsModerate()
    {
        SetupChatResponse("""{"complexity":"moderate","confidence":0.8,"reasoning":"Requires cross-referencing"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync("Compare the CRAG and Self-RAG approaches in our pipeline");

        result.Complexity.Should().Be(QueryComplexity.Moderate);
    }

    [Fact]
    public async Task ClassifyAsync_ComplexQuery_ReturnsComplex()
    {
        SetupChatResponse("""{"complexity":"complex","confidence":0.75,"reasoning":"Multi-hop reasoning needed"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            "Based on the architecture docs and the deployment guide, what changes are needed to support multi-tenant GraphRAG?");

        result.Complexity.Should().Be(QueryComplexity.Complex);
    }

    [Fact]
    public async Task ClassifyAsync_InvalidJson_FallsBackToModerate()
    {
        SetupChatResponse("I can't classify this properly");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync("Some query");

        result.Complexity.Should().Be(QueryComplexity.Moderate);
        result.Confidence.Should().Be(0.5);
    }

    [Fact]
    public async Task ClassifyAsync_LlmThrows_FallsBackToModerate()
    {
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync("Some query");

        result.Complexity.Should().Be(QueryComplexity.Moderate);
        result.Confidence.Should().Be(0.5);
    }

    [Fact]
    public async Task ClassifyAsync_ConfidenceClamped_StaysInRange()
    {
        SetupChatResponse("""{"complexity":"simple","confidence":1.5,"reasoning":"Over-confident"}""");
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync("Test query");

        result.Confidence.Should().BeInRange(0.0, 1.0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "QueryComplexityClassifierTests" --verbosity normal`
Expected: FAIL — `QueryComplexityClassifier` class does not exist.

- [ ] **Step 3: Implement QueryComplexityClassifier**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryComplexityClassifier.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.QueryTransform;

/// <summary>
/// LLM-based query complexity classifier that determines the retrieval cost tier.
/// Uses few-shot prompting to classify queries as Trivial, Simple, Moderate, or Complex.
/// Falls back to Moderate on any failure to ensure retrieval safety.
/// </summary>
public sealed class QueryComplexityClassifier : IQueryComplexityClassifier
{
    private readonly IRagModelRouter _modelRouter;
    private readonly ILogger<QueryComplexityClassifier> _logger;

    private const string SystemPrompt = """
        You are a query complexity classifier for a RAG system. Classify the user's query into exactly one complexity tier.

        **Tiers:**
        - **trivial**: General knowledge the LLM can answer without any document retrieval. Common facts, definitions, math, coding syntax.
          Examples: "What is the capital of France?", "What does SOLID stand for?", "Convert 5km to miles"
        - **simple**: Requires looking up a single fact or section from the knowledge base. One retrieval pass suffices.
          Examples: "What chunking strategies are configured?", "What is the default topK?", "How is the reranker registered?"
        - **moderate**: Requires cross-referencing multiple sections or comparing concepts. Needs hybrid retrieval + quality evaluation.
          Examples: "Compare CRAG and Self-RAG approaches", "How does the feedback scorer interact with the reranker?", "What are the tradeoffs between Azure and FAISS vector stores?"
        - **complex**: Requires synthesizing information across multiple documents, multi-hop reasoning, or iterative retrieval.
          Examples: "Based on the architecture and deployment docs, what changes support multi-tenant GraphRAG?", "Trace the full execution path from tool call to assembled context and identify all failure modes"

        Respond with JSON only: {"complexity": "trivial|simple|moderate|complex", "confidence": 0.0-1.0, "reasoning": "brief explanation"}
        """;

    public QueryComplexityClassifier(
        IRagModelRouter modelRouter,
        ILogger<QueryComplexityClassifier> logger)
    {
        _modelRouter = modelRouter;
        _logger = logger;
    }

    public async Task<ComplexityClassification> ClassifyAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _modelRouter.GetClientForOperation("complexity_classification");
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, query),
            };

            var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var content = response.Text?.Trim() ?? string.Empty;

            return ParseClassification(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Complexity classification failed for query, falling back to Moderate");
            return CreateFallback();
        }
    }

    private ComplexityClassification ParseClassification(string content)
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

            var complexityStr = root.GetProperty("complexity").GetString() ?? "moderate";
            var confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.5;
            var reasoning = root.TryGetProperty("reasoning", out var reason) ? reason.GetString() : null;

            var complexity = complexityStr.ToLowerInvariant() switch
            {
                "trivial" => QueryComplexity.Trivial,
                "simple" => QueryComplexity.Simple,
                "moderate" => QueryComplexity.Moderate,
                "complex" => QueryComplexity.Complex,
                _ => QueryComplexity.Moderate,
            };

            return new ComplexityClassification
            {
                Complexity = complexity,
                Confidence = Math.Clamp(confidence, 0.0, 1.0),
                Reasoning = reasoning,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse complexity classification JSON, falling back to Moderate");
            return CreateFallback();
        }
    }

    private static ComplexityClassification CreateFallback()
        => new()
        {
            Complexity = QueryComplexity.Moderate,
            Confidence = 0.5,
            Reasoning = "Classification failed; defaulting to Moderate for safety.",
        };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "QueryComplexityClassifierTests" --verbosity normal`
Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/QueryTransform/QueryComplexityClassifier.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/QueryTransform/QueryComplexityClassifierTests.cs
git commit -m "feat(rag): implement QueryComplexityClassifier with LLM few-shot classification"
```

---

### Task 6: Implementation — RetrievalDecisionGate

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalDecisionGateTests.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalDecisionGateTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using FluentAssertions;
using Infrastructure.AI.RAG.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

public sealed class RetrievalDecisionGateTests
{
    private RetrievalDecisionGate CreateGate(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(configure);
        return new RetrievalDecisionGate(
            config,
            Mock.Of<ILogger<RetrievalDecisionGate>>());
    }

    [Fact]
    public void Decide_TrivialQuery_SkipsRetrieval()
    {
        var gate = CreateGate();
        var classification = RagTestData.CreateTrivialClassification();

        var decision = gate.Decide(classification);

        decision.SkipRetrieval.Should().BeTrue();
        decision.Complexity.Should().Be(QueryComplexity.Trivial);
    }

    [Fact]
    public void Decide_SimpleQuery_UsesLightweightRetrieval()
    {
        var gate = CreateGate();
        var classification = RagTestData.CreateSimpleClassification();

        var decision = gate.Decide(classification);

        decision.SkipRetrieval.Should().BeFalse();
        decision.TopK.Should().Be(5);
        decision.UseReranking.Should().BeFalse();
        decision.UseCragEvaluation.Should().BeFalse();
        decision.Complexity.Should().Be(QueryComplexity.Simple);
    }

    [Fact]
    public void Decide_ModerateQuery_UsesFullPipeline()
    {
        var gate = CreateGate();
        var classification = RagTestData.CreateModerateClassification();

        var decision = gate.Decide(classification);

        decision.SkipRetrieval.Should().BeFalse();
        decision.TopK.Should().Be(10);
        decision.UseReranking.Should().BeTrue();
        decision.UseCragEvaluation.Should().BeTrue();
        decision.Complexity.Should().Be(QueryComplexity.Moderate);
    }

    [Fact]
    public void Decide_ComplexQuery_UsesEnhancedRetrieval()
    {
        var gate = CreateGate();
        var classification = RagTestData.CreateComplexClassification();

        var decision = gate.Decide(classification);

        decision.SkipRetrieval.Should().BeFalse();
        decision.TopK.Should().Be(15);
        decision.UseReranking.Should().BeTrue();
        decision.UseCragEvaluation.Should().BeTrue();
        decision.Complexity.Should().Be(QueryComplexity.Complex);
    }

    [Fact]
    public void Decide_LowConfidence_FallsBackToModerate()
    {
        var gate = CreateGate();
        var classification = new ComplexityClassification
        {
            Complexity = QueryComplexity.Trivial,
            Confidence = 0.4,
            Reasoning = "Low confidence classification",
        };

        var decision = gate.Decide(classification);

        decision.SkipRetrieval.Should().BeFalse();
        decision.UseReranking.Should().BeTrue();
        decision.UseCragEvaluation.Should().BeTrue();
        decision.Complexity.Should().Be(QueryComplexity.Moderate);
    }

    [Fact]
    public void Decide_CallerOverridesTopK_UsesCallerValue()
    {
        var gate = CreateGate();
        var classification = RagTestData.CreateSimpleClassification();

        var decision = gate.Decide(classification, requestedTopK: 20);

        decision.TopK.Should().Be(20);
    }

    [Fact]
    public void Decide_RoutingDisabled_AlwaysReturnsFullPipeline()
    {
        var gate = CreateGate(c => c.AI.Rag.ComplexityRouting.Enabled = false);
        var classification = RagTestData.CreateTrivialClassification();

        var decision = gate.Decide(classification);

        decision.SkipRetrieval.Should().BeFalse();
        decision.UseReranking.Should().BeTrue();
        decision.UseCragEvaluation.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "RetrievalDecisionGateTests" --verbosity normal`
Expected: FAIL — `RetrievalDecisionGate` class does not exist.

- [ ] **Step 3: Implement RetrievalDecisionGate**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.Orchestration;

/// <summary>
/// Determines the retrieval execution path based on query complexity classification.
/// When confidence is below threshold, falls back to Moderate (full pipeline) for safety.
/// When routing is disabled, always returns full pipeline parameters.
/// </summary>
public sealed class RetrievalDecisionGate : IRetrievalDecisionGate
{
    private readonly IOptionsMonitor<AppConfig> _config;
    private readonly ILogger<RetrievalDecisionGate> _logger;

    public RetrievalDecisionGate(
        IOptionsMonitor<AppConfig> config,
        ILogger<RetrievalDecisionGate> logger)
    {
        _config = config;
        _logger = logger;
    }

    public RetrievalDecision Decide(ComplexityClassification classification, int? requestedTopK = null)
    {
        var ragConfig = _config.CurrentValue.AI.Rag;
        var routingConfig = ragConfig.ComplexityRouting;

        if (!routingConfig.Enabled)
            return CreateFullPipelineDecision(ragConfig, requestedTopK);

        var effectiveComplexity = classification.Confidence < routingConfig.ConfidenceThreshold
            ? QueryComplexity.Moderate
            : classification.Complexity;

        if (effectiveComplexity != classification.Complexity)
        {
            _logger.LogDebug(
                "Complexity downgraded from {Original} to Moderate due to low confidence {Confidence:F2}",
                classification.Complexity, classification.Confidence);
        }

        return effectiveComplexity switch
        {
            QueryComplexity.Trivial => new RetrievalDecision
            {
                SkipRetrieval = true,
                TopK = 0,
                UseReranking = false,
                UseCragEvaluation = false,
                Complexity = QueryComplexity.Trivial,
            },
            QueryComplexity.Simple => new RetrievalDecision
            {
                SkipRetrieval = false,
                TopK = requestedTopK ?? routingConfig.SimpleTopK,
                UseReranking = !routingConfig.SkipRerankForSimple,
                UseCragEvaluation = !routingConfig.SkipCragForSimple,
                Complexity = QueryComplexity.Simple,
            },
            QueryComplexity.Complex => new RetrievalDecision
            {
                SkipRetrieval = false,
                TopK = requestedTopK ?? routingConfig.ComplexTopK,
                UseReranking = true,
                UseCragEvaluation = true,
                Complexity = QueryComplexity.Complex,
            },
            _ => CreateFullPipelineDecision(ragConfig, requestedTopK),
        };
    }

    private static RetrievalDecision CreateFullPipelineDecision(
        Domain.Common.Config.AI.RAG.RagConfig ragConfig,
        int? requestedTopK)
        => new()
        {
            SkipRetrieval = false,
            TopK = requestedTopK ?? ragConfig.ComplexityRouting.ModerateTopK ?? ragConfig.Retrieval.TopK,
            UseReranking = true,
            UseCragEvaluation = true,
            Complexity = QueryComplexity.Moderate,
        };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "RetrievalDecisionGateTests" --verbosity normal`
Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RetrievalDecisionGateTests.cs
git commit -m "feat(rag): implement RetrievalDecisionGate with tier-based routing logic"
```

---

### Task 7: Modify RagOrchestrator — Complexity-Routed Execution Paths

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs`
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorTests.cs`

- [ ] **Step 1: Write the new orchestrator tests**

Add these tests to the existing `RagOrchestratorTests.cs`:

```csharp
// Add to RagOrchestratorTests.cs

private readonly Mock<IQueryComplexityClassifier> _mockComplexityClassifier = new();
private readonly Mock<IRetrievalDecisionGate> _mockDecisionGate = new();

// Update CreateOrchestrator to accept the new dependencies (add to constructor call):
// _mockComplexityClassifier.Object,
// _mockDecisionGate.Object,

[Fact]
public async Task SearchAsync_TrivialQuery_SkipsRetrievalEntirely()
{
    _mockComplexityClassifier
        .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(RagTestData.CreateTrivialClassification());
    _mockDecisionGate
        .Setup(g => g.Decide(It.IsAny<ComplexityClassification>(), It.IsAny<int?>()))
        .Returns(new RetrievalDecision
        {
            SkipRetrieval = true, TopK = 0,
            UseReranking = false, UseCragEvaluation = false,
            Complexity = QueryComplexity.Trivial,
        });

    var orchestrator = CreateOrchestrator(c => c.AI.Rag.ComplexityRouting.Enabled = true);

    var result = await orchestrator.SearchAsync("What is 2+2?");

    result.AssembledText.Should().BeEmpty();
    _mockRetriever.Verify(
        r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
}

[Fact]
public async Task SearchAsync_SimpleQuery_SkipsRerankAndCrag()
{
    var retrievalResults = RagTestData.CreateRetrievalResults(3);
    _mockRetriever
        .Setup(r => r.RetrieveAsync(It.IsAny<string>(), 5, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(retrievalResults);
    _mockAssembler
        .Setup(a => a.AssembleAsync(It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(_expectedContext);
    _mockComplexityClassifier
        .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(RagTestData.CreateSimpleClassification());
    _mockDecisionGate
        .Setup(g => g.Decide(It.IsAny<ComplexityClassification>(), It.IsAny<int?>()))
        .Returns(new RetrievalDecision
        {
            SkipRetrieval = false, TopK = 5,
            UseReranking = false, UseCragEvaluation = false,
            Complexity = QueryComplexity.Simple,
        });

    var orchestrator = CreateOrchestrator(c => c.AI.Rag.ComplexityRouting.Enabled = true);

    var result = await orchestrator.SearchAsync("What is the default topK?");

    result.AssembledText.Should().Be("assembled text");
    _mockReranker.Verify(
        r => r.RerankAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Never);
    _mockCrag.Verify(
        c => c.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()),
        Times.Never);
}

[Fact]
public async Task SearchAsync_RoutingDisabled_UsesFullPipeline()
{
    SetupHappyPath();
    _mockComplexityClassifier
        .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(RagTestData.CreateTrivialClassification());

    var orchestrator = CreateOrchestrator(c => c.AI.Rag.ComplexityRouting.Enabled = false);

    var result = await orchestrator.SearchAsync("What is 2+2?");

    result.AssembledText.Should().Be("assembled text");
    _mockRetriever.Verify(
        r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 2: Run new tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "RagOrchestratorTests" --verbosity normal`
Expected: FAIL — constructor signature mismatch or missing complexity routing logic.

- [ ] **Step 3: Modify RagOrchestrator to accept new dependencies and route by complexity**

Add to the constructor:

```csharp
private readonly IQueryComplexityClassifier? _complexityClassifier;
private readonly IRetrievalDecisionGate? _decisionGate;

public RagOrchestrator(
    IHybridRetriever hybridRetriever,
    IReranker reranker,
    ICragEvaluator cragEvaluator,
    IRagContextAssembler contextAssembler,
    IGraphRagService graphRagService,
    IFeedbackWeightedScorer? feedbackScorer,
    QueryRouter queryRouter,
    IOptionsMonitor<AppConfig> config,
    ILogger<RagOrchestrator> logger,
    IQueryComplexityClassifier? complexityClassifier = null,
    IRetrievalDecisionGate? decisionGate = null)
{
    // ... existing assignments ...
    _complexityClassifier = complexityClassifier;
    _decisionGate = decisionGate;
}
```

Modify `SearchAsync` — insert complexity routing before the existing strategy classification:

```csharp
public async Task<RagAssembledContext> SearchAsync(
    string query, int? topK = null, string? collectionName = null,
    RetrievalStrategy? strategyOverride = null, CancellationToken cancellationToken = default)
{
    var ragConfig = _config.CurrentValue.AI.Rag;

    // --- NEW: Complexity-based routing ---
    if (ragConfig.ComplexityRouting.Enabled
        && _complexityClassifier is not null
        && _decisionGate is not null
        && strategyOverride is null)
    {
        var complexity = await _complexityClassifier.ClassifyAsync(query, cancellationToken);
        var decision = _decisionGate.Decide(complexity, topK);

        _logger.LogDebug(
            "Complexity routing: {Complexity} (confidence {Confidence:F2}), skip={Skip}",
            decision.Complexity, complexity.Confidence, decision.SkipRetrieval);

        if (decision.SkipRetrieval)
        {
            return new RagAssembledContext
            {
                AssembledText = string.Empty,
                TotalTokens = 0,
                WasTruncated = false,
            };
        }

        return await ExecuteRoutedPipelineAsync(
            query, decision, collectionName, cancellationToken);
    }

    // --- Existing logic (unchanged) ---
    // ... existing ClassifyStrategy + ExecuteVectorPipelineAsync / ExecuteGraphRagAsync ...
}
```

Add the new routed pipeline method:

```csharp
private async Task<RagAssembledContext> ExecuteRoutedPipelineAsync(
    string query, RetrievalDecision decision, string? collectionName,
    CancellationToken cancellationToken)
{
    // Step 1: Retrieve with decision's topK
    var candidates = await _hybridRetriever.RetrieveAsync(
        query, decision.TopK, collectionName, cancellationToken);

    if (candidates.Count == 0)
        return RagAssembledContext.Empty;

    IReadOnlyList<RerankedResult> reranked;

    // Step 2: Conditional reranking
    if (decision.UseReranking)
    {
        reranked = await _reranker.RerankAsync(
            query, candidates, decision.TopK, cancellationToken);
    }
    else
    {
        // Convert retrieval results directly to reranked results (preserving order)
        reranked = candidates.Select((r, i) => new RerankedResult
        {
            RetrievalResult = r,
            RerankScore = r.FusedScore,
            RerankRank = i + 1,
        }).ToList();
    }

    // Step 3: Conditional CRAG evaluation (with existing retry loop)
    if (decision.UseCragEvaluation)
    {
        // Delegate to existing CRAG pipeline logic
        return await ExecuteWithCragLoopAsync(
            query, reranked, candidates, collectionName, cancellationToken);
    }

    // Step 4: Direct assembly (skip CRAG)
    return await _contextAssembler.AssembleAsync(reranked, DefaultMaxTokens, cancellationToken);
}
```

- [ ] **Step 4: Run all orchestrator tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "RagOrchestratorTests" --verbosity normal`
Expected: All tests PASS (new + existing).

- [ ] **Step 5: Run full test suite for regression check**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --verbosity normal`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/RagOrchestratorTests.cs
git commit -m "feat(rag): integrate complexity routing into RagOrchestrator with tiered execution paths"
```

---

### Task 8: Modify DocumentSearchTool — Agent-Controlled Parameters

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI/Tools/DocumentSearchTool.cs`

- [ ] **Step 1: Add optional parameters to the tool schema**

Extend the tool's parameter schema to accept optional `top_k`, `strategy`, and `complexity_hint` from the agent:

```csharp
// In the operations/parameters definition, add:

// For "search" operation — add optional parameters:
// "top_k": integer, optional — override the default topK
// "strategy": string, optional — force a specific retrieval strategy ("hybrid", "graph", "simple")
// "complexity_hint": string, optional — hint to the complexity classifier ("trivial", "simple", "moderate", "complex")
```

Update `ExecuteAsync` to extract and pass these parameters:

```csharp
// In ExecuteAsync, after extracting "query":
var topK = parameters.TryGetValue("top_k", out var topKVal)
    ? int.TryParse(topKVal?.ToString(), out var k) ? k : (int?)null
    : null;

var strategyOverride = parameters.TryGetValue("strategy", out var stratVal)
    ? ParseStrategy(stratVal?.ToString())
    : null;

var result = await _orchestrator.SearchAsync(query, topK, collectionName: null, strategyOverride, cancellationToken);
```

- [ ] **Step 2: Build and run existing tool tests**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/Content/Tests/Infrastructure.AI.Tests/ --verbosity normal`
Expected: Build succeeds, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Tools/DocumentSearchTool.cs
git commit -m "feat(rag): extend DocumentSearchTool with agent-controlled topK and strategy parameters"
```

---

### Task 9: DI Registration — Wire New Services

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs`

- [ ] **Step 1: Register new services in DependencyInjection.cs**

Add a new `AddRagComplexityRouting` method and call it from `AddRagDependencies`:

```csharp
private static void AddRagComplexityRouting(IServiceCollection services)
{
    services.AddSingleton<IQueryComplexityClassifier, QueryComplexityClassifier>();
    services.AddSingleton<IRetrievalDecisionGate, RetrievalDecisionGate>();
}
```

Call it from `AddRagDependencies`:

```csharp
public static IServiceCollection AddRagDependencies(
    this IServiceCollection services,
    AppConfig appConfig)
{
    AddRagIngestion(services, appConfig);
    AddRagRetrieval(services, appConfig);
    AddRagQueryTransform(services, appConfig);
    AddRagEvaluation(services);
    AddRagGraphRag(services, appConfig);
    AddRagComplexityRouting(services);  // NEW
    AddRagOrchestration(services);
    return services;
}
```

- [ ] **Step 2: Build and run full test suite**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx --verbosity normal`
Expected: Build succeeds, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs
git commit -m "feat(rag): register QueryComplexityClassifier and RetrievalDecisionGate in DI"
```

---

### Task 10: Integration Test — End-to-End Complexity Routing

**Files:**
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/ComplexityRoutingIntegrationTests.cs`

- [ ] **Step 1: Write integration tests covering the full routing flow**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/ComplexityRoutingIntegrationTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

/// <summary>
/// Integration tests verifying the full complexity routing flow:
/// Classifier → Decision Gate → Orchestrator pipeline path.
/// </summary>
public sealed class ComplexityRoutingIntegrationTests
{
    private readonly Mock<IHybridRetriever> _mockRetriever = new();
    private readonly Mock<IReranker> _mockReranker = new();
    private readonly Mock<ICragEvaluator> _mockCrag = new();
    private readonly Mock<IRagContextAssembler> _mockAssembler = new();
    private readonly Mock<IGraphRagService> _mockGraphRag = new();
    private readonly Mock<IQueryComplexityClassifier> _mockClassifier = new();

    private RagOrchestrator CreateOrchestrator(Action<AppConfig>? configure = null)
    {
        var config = RagTestData.CreateConfigMonitor(configure);
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
            gate);
    }

    [Fact]
    public async Task TrivialQuery_NoRetrieverCalled_ReturnsEmptyContext()
    {
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateTrivialClassification(0.95));

        var orchestrator = CreateOrchestrator(c => c.AI.Rag.ComplexityRouting.Enabled = true);
        var result = await orchestrator.SearchAsync("What is 2+2?");

        result.AssembledText.Should().BeEmpty();
        result.TotalTokens.Should().Be(0);
        _mockRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockReranker.Verify(
            r => r.RerankAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockCrag.Verify(
            c => c.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SimpleQuery_RetrievesButSkipsRerankAndCrag()
    {
        var results = RagTestData.CreateRetrievalResults(3);
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), 5, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);
        _mockAssembler
            .Setup(a => a.AssembleAsync(It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext { AssembledText = "simple result", TotalTokens = 50 });
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateSimpleClassification(0.9));

        var orchestrator = CreateOrchestrator(c => c.AI.Rag.ComplexityRouting.Enabled = true);
        var result = await orchestrator.SearchAsync("What is the default topK?");

        result.AssembledText.Should().Be("simple result");
        _mockRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), 5, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockReranker.Verify(
            r => r.RerankAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockCrag.Verify(
            c => c.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LowConfidenceTrivial_UpgradedToModerate_UsesFullPipeline()
    {
        var retrievalResults = RagTestData.CreateRetrievalResults(3);
        var rerankedResults = RagTestData.CreateRerankedResults(3);
        _mockRetriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrievalResults);
        _mockReranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rerankedResults);
        _mockCrag
            .Setup(c => c.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RagTestData.CreateAcceptEvaluation());
        _mockAssembler
            .Setup(a => a.AssembleAsync(It.IsAny<IReadOnlyList<RerankedResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagAssembledContext { AssembledText = "full result", TotalTokens = 100 });
        _mockClassifier
            .Setup(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplexityClassification
            {
                Complexity = QueryComplexity.Trivial,
                Confidence = 0.4,
                Reasoning = "Low confidence",
            });

        var orchestrator = CreateOrchestrator(c => c.AI.Rag.ComplexityRouting.Enabled = true);
        var result = await orchestrator.SearchAsync("Ambiguous query");

        result.AssembledText.Should().Be("full result");
        _mockRetriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockReranker.Verify(
            r => r.RerankAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests/ --filter "ComplexityRoutingIntegrationTests" --verbosity normal`
Expected: All 3 tests PASS.

- [ ] **Step 3: Run full solution test suite**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx --verbosity normal`
Expected: Build succeeds, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/ComplexityRoutingIntegrationTests.cs
git commit -m "test(rag): add complexity routing integration tests covering all tiers"
```

---

### Task 11: OTel Metrics — Complexity Routing Observability

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs`

- [ ] **Step 1: Add complexity-routing OTel tags to the orchestrator's Activity**

In the `SearchAsync` method, after the complexity decision is made, tag the current activity:

```csharp
using System.Diagnostics;

// After complexity classification:
Activity.Current?.SetTag("rag.query.complexity", decision.Complexity.ToString());
Activity.Current?.SetTag("rag.query.skip_retrieval", decision.SkipRetrieval);
Activity.Current?.SetTag("rag.query.complexity_confidence", complexity.Confidence);
Activity.Current?.SetTag("rag.query.use_reranking", decision.UseReranking);
Activity.Current?.SetTag("rag.query.use_crag", decision.UseCragEvaluation);
Activity.Current?.SetTag("rag.query.effective_top_k", decision.TopK);
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx --verbosity normal`
Expected: Build succeeds, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/RagOrchestrator.cs
git commit -m "feat(rag): add OTel tags for complexity routing decisions"
```

---

## Phase A Verification Checklist

After completing all tasks, verify:

- [ ] `dotnet build src/AgenticHarness.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test src/AgenticHarness.slnx` — all tests pass
- [ ] New tests cover: classifier (7 tests), decision gate (7 tests), orchestrator routing (3 tests), integration (3 tests) = **20 new tests**
- [ ] No existing test regressions
- [ ] Config: `AppConfig.AI.Rag.ComplexityRouting.Enabled = false` preserves current behavior exactly
- [ ] Config: `AppConfig.AI.Rag.ComplexityRouting.Enabled = true` activates routing
- [ ] OTel tags visible in Jaeger/Azure Monitor traces
