# Agentic RAG — Extensible Retrieval Sources Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded source switch in `MultiSourceOrchestrator` with a keyed DI `IRetrievalSource` abstraction, then add Web Search and SQL Database as new pluggable retrieval sources.

**Architecture:** Introduce `IRetrievalSource` interface registered via keyed DI (same pattern as `IReranker`, `IChunkingService`). Wrap existing `IHybridRetriever` and `IGraphRagService` as adapters. Add `BingWebSearchProvider` behind `IWebSearchProvider` (provider-agnostic) and a two-tier SQL source (template-first, LLM text-to-SQL fallback with read-only enforcement).

**Tech Stack:** .NET 10, keyed DI, `Azure.Search.Documents` (Bing), `Microsoft.Data.Sqlite` / `System.Data.Common` (SQL), `Microsoft.Extensions.AI` (LLM calls), xUnit/Moq/FluentAssertions (tests).

**Spec:** `docs/superpowers/specs/2026-05-20-agentic-rag-extensible-sources-design.md`

---

## Task 1: Foundation — Interfaces, Domain Models, Config

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalSource.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/IWebSearchProvider.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/ISqlQueryTemplateStore.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/RAG/ISqlQueryExecutor.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/WebSearchResult.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/SqlQueryTemplate.cs`
- Create: `src/Content/Domain/Domain.AI/RAG/Models/SqlRetrievalResult.cs`
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/WebSearchConfig.cs`
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/SqlDatabaseConfig.cs`
- Modify: `src/Content/Domain/Domain.Common/Config/AI/RAG/MultiSourceConfig.cs`
- Modify: `src/Content/Domain/Domain.AI/Telemetry/Conventions/RagConventions.cs`

- [ ] **Step 1: Create `IRetrievalSource.cs`**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalSource.cs
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Pluggable retrieval source resolved via keyed DI.
/// Each implementation is registered with a string key (e.g., "vector", "graph", "web_search", "sql_database").
/// The <see cref="IMultiSourceOrchestrator"/> resolves enabled sources by key and fans out retrieval in parallel.
/// </summary>
public interface IRetrievalSource
{
    /// <summary>
    /// Unique identifier for this source, matching the keyed DI registration key.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Executes retrieval against this source and returns results with per-source latency and token metrics.
    /// </summary>
    Task<SourceRetrievalResult> RetrieveAsync(
        string query,
        int topK,
        QueryComplexity complexity,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Create `IWebSearchProvider.cs`**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/IWebSearchProvider.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Provider-agnostic web search contract. Implementations: Bing, Tavily, Google.
/// Registered via keyed DI with the provider name (e.g., "bing").
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Executes a web search and returns structured results with title, snippet, URL, and optional full content.
    /// </summary>
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Create `ISqlQueryTemplateStore.cs` and `ISqlQueryExecutor.cs`**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/ISqlQueryTemplateStore.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Loads pre-defined SQL query templates from a backing store (JSON file, database, etc.).
/// Templates are matched against natural language queries before falling back to LLM-generated SQL.
/// </summary>
public interface ISqlQueryTemplateStore
{
    Task<IReadOnlyList<SqlQueryTemplate>> GetTemplatesAsync(CancellationToken cancellationToken);
}
```

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/RAG/ISqlQueryExecutor.cs
using Domain.AI.RAG.Models;

namespace Application.AI.Common.Interfaces.RAG;

/// <summary>
/// Executes validated SQL queries against a configured database with safety guardrails
/// (read-only enforcement, row limits, query timeout).
/// </summary>
public interface ISqlQueryExecutor
{
    Task<SqlRetrievalResult> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Create domain models — `WebSearchResult.cs`, `SqlQueryTemplate.cs`, `SqlRetrievalResult.cs`**

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/WebSearchResult.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// A single result from a web search provider (Bing, Tavily, Google).
/// </summary>
public sealed record WebSearchResult
{
    /// <summary>Page title.</summary>
    public required string Title { get; init; }

    /// <summary>Search engine snippet or extracted summary.</summary>
    public required string Snippet { get; init; }

    /// <summary>Full URL of the result page.</summary>
    public required string Url { get; init; }

    /// <summary>Full-text content if the provider supports extraction (e.g., Tavily). Null otherwise.</summary>
    public string? Content { get; init; }
}
```

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/SqlQueryTemplate.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// A pre-defined parameterized SQL query template. Matched against natural language queries
/// before falling back to LLM-generated SQL.
/// </summary>
public sealed record SqlQueryTemplate
{
    /// <summary>Unique template identifier (e.g., "orders_by_date").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description used by the LLM to match queries to templates.</summary>
    public required string Description { get; init; }

    /// <summary>Parameterized SQL (e.g., "SELECT * FROM orders WHERE date >= @startDate").</summary>
    public required string SqlTemplate { get; init; }

    /// <summary>Parameter names expected by the template (e.g., ["startDate", "endDate"]).</summary>
    public required IReadOnlyList<string> Parameters { get; init; }
}
```

```csharp
// src/Content/Domain/Domain.AI/RAG/Models/SqlRetrievalResult.cs
namespace Domain.AI.RAG.Models;

/// <summary>
/// Result of executing a SQL query, including the query text, whether it matched a template,
/// and the result rows as dictionaries.
/// </summary>
public sealed record SqlRetrievalResult
{
    /// <summary>The SQL query that was executed.</summary>
    public required string Query { get; init; }

    /// <summary>True if the query matched a pre-defined template; false if LLM-generated.</summary>
    public required bool WasTemplateMatch { get; init; }

    /// <summary>Result rows as column-name → value dictionaries.</summary>
    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }
}
```

- [ ] **Step 5: Create config POCOs — `WebSearchConfig.cs`, `SqlDatabaseConfig.cs`**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/WebSearchConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for the web search retrieval source.
/// Bound to <c>AppConfig:AI:Rag:WebSearch</c>.
/// </summary>
public sealed class WebSearchConfig
{
    /// <summary>Provider key matching a keyed DI registration (e.g., "bing", "tavily").</summary>
    public string Provider { get; set; } = "bing";

    /// <summary>Provider endpoint override. Null uses the provider's default.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Maximum results per query.</summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>Search market/locale (e.g., "en-US").</summary>
    public string Market { get; set; } = "en-US";

    /// <summary>Safe search level: Off, Moderate, Strict.</summary>
    public string SafeSearch { get; set; } = "Moderate";
}
```

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/SqlDatabaseConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for the SQL database retrieval source.
/// Bound to <c>AppConfig:AI:Rag:SqlDatabase</c>.
/// Disabled by default — opt-in only.
/// </summary>
public sealed class SqlDatabaseConfig
{
    /// <summary>Enable/disable the SQL database retrieval source.</summary>
    public bool Enabled { get; set; }

    /// <summary>Path to the JSON file containing SQL query templates.</summary>
    public string TemplatesPath { get; set; } = "sql-templates.json";

    /// <summary>Allow LLM-generated SQL when no template matches. Set false for high-security environments.</summary>
    public bool AllowLlmFallback { get; set; } = true;

    /// <summary>Maximum rows returned per query.</summary>
    public int MaxRows { get; set; } = 100;

    /// <summary>Query execution timeout in seconds.</summary>
    public int QueryTimeoutSeconds { get; set; } = 5;

    /// <summary>Minimum confidence threshold (0.0-1.0) for template matching.</summary>
    public double TemplateMatchConfidenceThreshold { get; set; } = 0.7;
}
```

- [ ] **Step 6: Update `MultiSourceConfig.cs` — add `SourcesByComplexity`**

Add this property to the existing `MultiSourceConfig` class:

```csharp
/// <summary>
/// Maps each <see cref="QueryComplexity"/> tier to the source names that should be queried.
/// Filtered at runtime by <see cref="EnabledSources"/> — a source must appear in both lists.
/// </summary>
public Dictionary<string, List<string>> SourcesByComplexity { get; set; } = new()
{
    ["Trivial"] = ["vector"],
    ["Simple"] = ["vector"],
    ["Moderate"] = ["vector", "graph"],
    ["Complex"] = ["vector", "graph", "web_search", "sql_database"]
};
```

- [ ] **Step 7: Update `RagConventions.cs` — add web search and SQL source constants**

Add these constants to the existing `RagConventions` class:

```csharp
// Web search source telemetry
public const string WebSearchProvider = "rag.web_search.provider";
public const string WebSearchResultCount = "rag.web_search.result_count";
public const string WebSearchLatency = "rag.web_search.latency_ms";

// SQL database source telemetry
public const string SqlSourceTemplate = "rag.sql.template_name";
public const string SqlSourceWasTemplate = "rag.sql.was_template_match";
public const string SqlSourceRowCount = "rag.sql.row_count";
public const string SqlSourceLatency = "rag.sql.latency_ms";
```

- [ ] **Step 8: Build to verify all new types compile**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeded with 0 errors.

- [ ] **Step 9: Commit foundation**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/RAG/IRetrievalSource.cs
git add src/Content/Application/Application.AI.Common/Interfaces/RAG/IWebSearchProvider.cs
git add src/Content/Application/Application.AI.Common/Interfaces/RAG/ISqlQueryTemplateStore.cs
git add src/Content/Application/Application.AI.Common/Interfaces/RAG/ISqlQueryExecutor.cs
git add src/Content/Domain/Domain.AI/RAG/Models/WebSearchResult.cs
git add src/Content/Domain/Domain.AI/RAG/Models/SqlQueryTemplate.cs
git add src/Content/Domain/Domain.AI/RAG/Models/SqlRetrievalResult.cs
git add src/Content/Domain/Domain.Common/Config/AI/RAG/WebSearchConfig.cs
git add src/Content/Domain/Domain.Common/Config/AI/RAG/SqlDatabaseConfig.cs
git add src/Content/Domain/Domain.Common/Config/AI/RAG/MultiSourceConfig.cs
git add src/Content/Domain/Domain.AI/Telemetry/Conventions/RagConventions.cs
git commit -m "feat(rag): add IRetrievalSource abstraction, web search and SQL domain models"
```

---

## Task 2: Vector and Graph Retrieval Source Adapters

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/VectorRetrievalSource.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/GraphRetrievalSource.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/VectorRetrievalSourceTests.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/GraphRetrievalSourceTests.cs`

- [ ] **Step 1: Write failing test for `VectorRetrievalSource`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/VectorRetrievalSourceTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

public sealed class VectorRetrievalSourceTests
{
    private readonly Mock<IHybridRetriever> _retriever = new();

    [Fact]
    public async Task RetrieveAsync_DelegatesToHybridRetriever_ReturnsWrappedResult()
    {
        var chunk = CreateChunk("chunk-1");
        var expected = new RetrievalResult
        {
            Chunk = chunk,
            DenseScore = 0.9,
            SparseScore = 0.7,
            FusedScore = 0.85
        };
        _retriever
            .Setup(r => r.RetrieveAsync("test query", 10, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);

        var sut = new VectorRetrievalSource(_retriever.Object);

        var result = await sut.RetrieveAsync("test query", 10, QueryComplexity.Moderate, CancellationToken.None);

        result.SourceName.Should().Be("vector");
        result.Results.Should().HaveCount(1);
        result.Results[0].FusedScore.Should().Be(0.85);
        result.Latency.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void SourceName_IsVector()
    {
        var sut = new VectorRetrievalSource(_retriever.Object);
        sut.SourceName.Should().Be("vector");
    }

    private static DocumentChunk CreateChunk(string id) => new()
    {
        Id = id,
        DocumentId = "doc-1",
        SectionPath = "Section > Test",
        Content = "Test content",
        Tokens = 10,
        Metadata = new ChunkMetadata
        {
            SourceFileName = "test.md",
            ChunkIndex = 0,
            TotalChunks = 1
        }
    };
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "VectorRetrievalSourceTests" --no-build`
Expected: FAIL — `VectorRetrievalSource` does not exist yet.

- [ ] **Step 3: Implement `VectorRetrievalSource`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/VectorRetrievalSource.cs
using System.Diagnostics;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;

namespace Infrastructure.AI.RAG.Orchestration;

/// <summary>
/// Adapts <see cref="IHybridRetriever"/> to the <see cref="IRetrievalSource"/> contract.
/// Registered as keyed DI with key "vector".
/// </summary>
internal sealed class VectorRetrievalSource(IHybridRetriever hybridRetriever) : IRetrievalSource
{
    public string SourceName => "vector";

    public async Task<SourceRetrievalResult> RetrieveAsync(
        string query, int topK, QueryComplexity complexity, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var results = await hybridRetriever.RetrieveAsync(query, topK, cancellationToken: cancellationToken);
        sw.Stop();

        return new SourceRetrievalResult
        {
            SourceName = SourceName,
            Results = results,
            Latency = sw.Elapsed,
            TokensUsed = 0
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "VectorRetrievalSourceTests"`
Expected: 2 passed.

- [ ] **Step 5: Write failing test for `GraphRetrievalSource`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/GraphRetrievalSourceTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

public sealed class GraphRetrievalSourceTests
{
    private readonly Mock<IGraphRagService> _graphRag = new();

    [Fact]
    public async Task RetrieveAsync_DelegatesToLocalSearch_ReturnsWrappedResult()
    {
        var chunk = new DocumentChunk
        {
            Id = "graph-1",
            DocumentId = "doc-1",
            SectionPath = "Entity > Test",
            Content = "Graph result content",
            Tokens = 15,
            Metadata = new ChunkMetadata { SourceFileName = "graph.md", ChunkIndex = 0, TotalChunks = 1 }
        };
        var expected = new RetrievalResult
        {
            Chunk = chunk,
            DenseScore = 0.0,
            SparseScore = 0.0,
            FusedScore = 0.8
        };
        _graphRag
            .Setup(g => g.LocalSearchAsync("test query", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);

        var sut = new GraphRetrievalSource(_graphRag.Object);

        var result = await sut.RetrieveAsync("test query", 10, QueryComplexity.Moderate, CancellationToken.None);

        result.SourceName.Should().Be("graph");
        result.Results.Should().HaveCount(1);
        result.Latency.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void SourceName_IsGraph()
    {
        var sut = new GraphRetrievalSource(_graphRag.Object);
        sut.SourceName.Should().Be("graph");
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "GraphRetrievalSourceTests" --no-build`
Expected: FAIL — `GraphRetrievalSource` does not exist yet.

- [ ] **Step 7: Implement `GraphRetrievalSource`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/GraphRetrievalSource.cs
using System.Diagnostics;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;

namespace Infrastructure.AI.RAG.Orchestration;

/// <summary>
/// Adapts <see cref="IGraphRagService"/> to the <see cref="IRetrievalSource"/> contract.
/// Uses <see cref="IGraphRagService.LocalSearchAsync"/> for entity-neighborhood retrieval.
/// Registered as keyed DI with key "graph".
/// </summary>
internal sealed class GraphRetrievalSource(IGraphRagService graphRagService) : IRetrievalSource
{
    public string SourceName => "graph";

    public async Task<SourceRetrievalResult> RetrieveAsync(
        string query, int topK, QueryComplexity complexity, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var results = await graphRagService.LocalSearchAsync(query, topK, cancellationToken);
        sw.Stop();

        return new SourceRetrievalResult
        {
            SourceName = SourceName,
            Results = results,
            Latency = sw.Elapsed,
            TokensUsed = 0
        };
    }
}
```

- [ ] **Step 8: Run all adapter tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "VectorRetrievalSourceTests|GraphRetrievalSourceTests"`
Expected: 4 passed.

- [ ] **Step 9: Commit adapters**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/VectorRetrievalSource.cs
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/GraphRetrievalSource.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/VectorRetrievalSourceTests.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/GraphRetrievalSourceTests.cs
git commit -m "feat(rag): add Vector and Graph retrieval source adapters"
```

---

## Task 3: MultiSourceOrchestrator Refactor

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorRefactorTests.cs`

- [ ] **Step 1: Write failing test for keyed DI source resolution**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorRefactorTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

public sealed class MultiSourceOrchestratorRefactorTests
{
    [Fact]
    public async Task RetrieveFromAllSourcesAsync_ResolvesSourcesByKeyFromDI()
    {
        var vectorResult = new SourceRetrievalResult
        {
            SourceName = "vector",
            Results = [CreateRetrievalResult("chunk-v1", 0.9)],
            Latency = TimeSpan.FromMilliseconds(50),
            TokensUsed = 0
        };
        var mockVector = new Mock<IRetrievalSource>();
        mockVector.Setup(s => s.SourceName).Returns("vector");
        mockVector
            .Setup(s => s.RetrieveAsync("test", 5, QueryComplexity.Simple, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResult);

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IRetrievalSource>("vector", mockVector.Object);

        var config = CreateConfig(enabledSources: ["vector"], sourcesByComplexity: new()
        {
            ["Simple"] = ["vector"]
        });

        var costTracker = new Mock<IRetrievalCostTracker>();
        var sp = services.BuildServiceProvider();

        var sut = new MultiSourceOrchestrator(
            sp, costTracker.Object, config, NullLogger<MultiSourceOrchestrator>.Instance);

        var results = await sut.RetrieveFromAllSourcesAsync("test", 5, QueryComplexity.Simple);

        results.Should().HaveCount(1);
        results[0].FusedScore.Should().Be(0.9);
        mockVector.Verify(s => s.RetrieveAsync("test", 5, QueryComplexity.Simple, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_SkipsDisabledSources()
    {
        var mockVector = new Mock<IRetrievalSource>();
        mockVector.Setup(s => s.SourceName).Returns("vector");
        mockVector
            .Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceRetrievalResult
            {
                SourceName = "vector", Results = [], Latency = TimeSpan.Zero, TokensUsed = 0
            });

        var mockGraph = new Mock<IRetrievalSource>();
        mockGraph.Setup(s => s.SourceName).Returns("graph");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IRetrievalSource>("vector", mockVector.Object);
        services.AddKeyedSingleton<IRetrievalSource>("graph", mockGraph.Object);

        // graph is registered in DI but NOT in EnabledSources
        var config = CreateConfig(enabledSources: ["vector"], sourcesByComplexity: new()
        {
            ["Simple"] = ["vector", "graph"]
        });

        var sp = services.BuildServiceProvider();
        var sut = new MultiSourceOrchestrator(
            sp, Mock.Of<IRetrievalCostTracker>(), config, NullLogger<MultiSourceOrchestrator>.Instance);

        await sut.RetrieveFromAllSourcesAsync("test", 5, QueryComplexity.Simple);

        mockGraph.Verify(
            s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RetrieveFromAllSourcesAsync_GracefullyHandlesMissingSource()
    {
        var services = new ServiceCollection();
        // "web_search" is enabled but not registered — should log warning and skip
        var config = CreateConfig(enabledSources: ["web_search"], sourcesByComplexity: new()
        {
            ["Simple"] = ["web_search"]
        });

        var sp = services.BuildServiceProvider();
        var sut = new MultiSourceOrchestrator(
            sp, Mock.Of<IRetrievalCostTracker>(), config, NullLogger<MultiSourceOrchestrator>.Instance);

        var results = await sut.RetrieveFromAllSourcesAsync("test", 5, QueryComplexity.Simple);

        results.Should().BeEmpty();
    }

    private static IOptionsMonitor<AppConfig> CreateConfig(
        List<string> enabledSources,
        Dictionary<string, List<string>> sourcesByComplexity)
    {
        var appConfig = new AppConfig();
        appConfig.AI.Rag.MultiSource.EnabledSources = enabledSources;
        appConfig.AI.Rag.MultiSource.SourcesByComplexity = sourcesByComplexity;
        appConfig.AI.Rag.MultiSource.SourceTimeout = TimeSpan.FromSeconds(5);

        var mock = new Mock<IOptionsMonitor<AppConfig>>();
        mock.Setup(m => m.CurrentValue).Returns(appConfig);
        return mock.Object;
    }

    private static RetrievalResult CreateRetrievalResult(string chunkId, double fusedScore) => new()
    {
        Chunk = new DocumentChunk
        {
            Id = chunkId,
            DocumentId = "doc-1",
            SectionPath = "Test",
            Content = "Content",
            Tokens = 10,
            Metadata = new ChunkMetadata { SourceFileName = "test.md", ChunkIndex = 0, TotalChunks = 1 }
        },
        DenseScore = fusedScore,
        SparseScore = 0.0,
        FusedScore = fusedScore
    };
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "MultiSourceOrchestratorRefactorTests" --no-build`
Expected: FAIL — constructor signature doesn't match.

- [ ] **Step 3: Refactor `MultiSourceOrchestrator` to use keyed DI**

Modify `src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs`:

Replace the constructor and field declarations:

```csharp
// REPLACE the old constructor and fields with:
private readonly IServiceProvider _serviceProvider;
private readonly IRetrievalCostTracker _costTracker;
private readonly IOptionsMonitor<AppConfig> _configMonitor;
private readonly ILogger<MultiSourceOrchestrator> _logger;

/// <summary>
/// Creates a new <see cref="MultiSourceOrchestrator"/> that resolves
/// <see cref="IRetrievalSource"/> implementations by key from the DI container.
/// </summary>
public MultiSourceOrchestrator(
    IServiceProvider serviceProvider,
    IRetrievalCostTracker costTracker,
    IOptionsMonitor<AppConfig> configMonitor,
    ILogger<MultiSourceOrchestrator> logger)
{
    _serviceProvider = serviceProvider;
    _costTracker = costTracker;
    _configMonitor = configMonitor;
    _logger = logger;
}
```

Replace the `DetermineSourcesForComplexity` method to use config-driven mapping:

```csharp
private IReadOnlyList<string> DetermineSourcesForComplexity(QueryComplexity complexity)
{
    var config = _configMonitor.CurrentValue.AI.Rag.MultiSource;
    var complexityKey = complexity.ToString();

    if (!config.SourcesByComplexity.TryGetValue(complexityKey, out var candidates))
        candidates = ["vector"];

    return candidates
        .Where(s => config.EnabledSources.Contains(s, StringComparer.OrdinalIgnoreCase))
        .ToList();
}
```

Replace `ExecuteSourceWithTimeoutAsync` to use keyed DI resolution:

```csharp
private async Task<SourceRetrievalResult?> ExecuteSourceWithTimeoutAsync(
    string sourceName, string query, int topK, QueryComplexity complexity,
    TimeSpan timeout, CancellationToken cancellationToken)
{
    var source = _serviceProvider.GetKeyedService<IRetrievalSource>(sourceName);
    if (source is null)
    {
        _logger.LogWarning("Retrieval source '{SourceName}' is enabled but not registered in DI", sourceName);
        return null;
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout);

    try
    {
        return await source.RetrieveAsync(query, topK, complexity, timeoutCts.Token);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        _logger.LogWarning("Retrieval source '{SourceName}' timed out after {Timeout}ms",
            sourceName, timeout.TotalMilliseconds);
        return null;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Retrieval source '{SourceName}' failed", sourceName);
        return null;
    }
}
```

Remove the old `_hybridRetriever` and `_graphRagService` fields and the old `ExecuteWebSearchAsync` method.

- [ ] **Step 4: Add `using Microsoft.Extensions.DependencyInjection;`** to the orchestrator file for `GetKeyedService`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "MultiSourceOrchestratorRefactorTests"`
Expected: 3 passed.

- [ ] **Step 6: Run full test suite to check for regressions**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests`
Expected: All existing tests pass. If any `MultiSourceOrchestratorTests` fail due to old constructor signature, update them to use the new `IServiceProvider`-based constructor with keyed mocks.

- [ ] **Step 7: Commit orchestrator refactor**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorRefactorTests.cs
git commit -m "refactor(rag): replace hardcoded source switch with keyed DI resolution"
```

---

## Task 4: Web Search — Bing Provider and Retrieval Source Adapter

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/WebSearch/BingWebSearchProvider.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/WebSearch/WebSearchRetrievalSource.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/WebSearch/BingWebSearchProviderTests.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/WebSearch/WebSearchRetrievalSourceTests.cs`

- [ ] **Step 1: Write failing test for `BingWebSearchProvider`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/WebSearch/BingWebSearchProviderTests.cs
using System.Net;
using System.Text.Json;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.WebSearch;

public sealed class BingWebSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_ParsesBingApiResponse_ReturnsStructuredResults()
    {
        var bingResponse = new
        {
            webPages = new
            {
                value = new[]
                {
                    new { name = "Result 1", snippet = "Snippet 1", url = "https://example.com/1" },
                    new { name = "Result 2", snippet = "Snippet 2", url = "https://example.com/2" }
                }
            }
        };
        var json = JsonSerializer.Serialize(bingResponse);
        var handler = new FakeHttpMessageHandler(json, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.bing.microsoft.com/") };

        var config = CreateConfig();
        var sut = new BingWebSearchProvider(httpClient, config, NullLogger<BingWebSearchProvider>.Instance);

        var results = await sut.SearchAsync("test query", 5, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].Title.Should().Be("Result 1");
        results[0].Snippet.Should().Be("Snippet 1");
        results[0].Url.Should().Be("https://example.com/1");
    }

    [Fact]
    public async Task SearchAsync_EmptyResponse_ReturnsEmptyList()
    {
        var bingResponse = new { webPages = new { value = Array.Empty<object>() } };
        var json = JsonSerializer.Serialize(bingResponse);
        var handler = new FakeHttpMessageHandler(json, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.bing.microsoft.com/") };

        var config = CreateConfig();
        var sut = new BingWebSearchProvider(httpClient, config, NullLogger<BingWebSearchProvider>.Instance);

        var results = await sut.SearchAsync("test query", 5, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ApiError_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler("error", HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.bing.microsoft.com/") };

        var config = CreateConfig();
        var sut = new BingWebSearchProvider(httpClient, config, NullLogger<BingWebSearchProvider>.Instance);

        var results = await sut.SearchAsync("test query", 5, CancellationToken.None);

        results.Should().BeEmpty();
    }

    private static IOptionsMonitor<AppConfig> CreateConfig()
    {
        var appConfig = new AppConfig();
        appConfig.AI.Rag.WebSearch = new WebSearchConfig
        {
            Provider = "bing",
            Market = "en-US",
            SafeSearch = "Moderate"
        };
        var mock = new Mock<IOptionsMonitor<AppConfig>>();
        mock.Setup(m => m.CurrentValue).Returns(appConfig);
        return mock.Object;
    }

    private sealed class FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "BingWebSearchProviderTests" --no-build`
Expected: FAIL — `BingWebSearchProvider` does not exist.

- [ ] **Step 3: Implement `BingWebSearchProvider`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/WebSearch/BingWebSearchProvider.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.WebSearch;

/// <summary>
/// Calls Bing Search API v7. API key must be provided via User Secrets or Key Vault,
/// injected as a named HttpClient with the <c>Ocp-Apim-Subscription-Key</c> header pre-configured.
/// </summary>
internal sealed class BingWebSearchProvider(
    HttpClient httpClient,
    IOptionsMonitor<AppConfig> configMonitor,
    ILogger<BingWebSearchProvider> logger) : IWebSearchProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken)
    {
        var config = configMonitor.CurrentValue.AI.Rag.WebSearch;
        var requestUri = $"v7.0/search?q={Uri.EscapeDataString(query)}&count={maxResults}&mkt={config.Market}&safeSearch={config.SafeSearch}";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bing Search API request failed for query '{Query}'", query);
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Bing Search API returned {StatusCode} for query '{Query}'",
                response.StatusCode, query);
            return [];
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var bingResponse = JsonSerializer.Deserialize<BingSearchResponse>(content, JsonOptions);

        if (bingResponse?.WebPages?.Value is null or { Length: 0 })
            return [];

        return bingResponse.WebPages.Value
            .Select(v => new WebSearchResult
            {
                Title = v.Name ?? "",
                Snippet = v.Snippet ?? "",
                Url = v.Url ?? "",
                Content = null
            })
            .ToList();
    }

    private sealed record BingSearchResponse(BingWebPages? WebPages);
    private sealed record BingWebPages(BingWebPage[] Value);
    private sealed record BingWebPage(string? Name, string? Snippet, string? Url);
}
```

- [ ] **Step 4: Run Bing provider tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "BingWebSearchProviderTests"`
Expected: 3 passed.

- [ ] **Step 5: Write failing test for `WebSearchRetrievalSource`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/WebSearch/WebSearchRetrievalSourceTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Moq;

namespace Infrastructure.AI.RAG.Tests.WebSearch;

public sealed class WebSearchRetrievalSourceTests
{
    private readonly Mock<IWebSearchProvider> _provider = new();

    [Fact]
    public async Task RetrieveAsync_ConvertsWebResultsToRetrievalResults()
    {
        _provider
            .Setup(p => p.SearchAsync("test", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new WebSearchResult { Title = "Page 1", Snippet = "Content 1", Url = "https://example.com/1" },
                new WebSearchResult { Title = "Page 2", Snippet = "Content 2", Url = "https://example.com/2" }
            ]);

        var sut = new WebSearchRetrievalSource(_provider.Object);

        var result = await sut.RetrieveAsync("test", 5, QueryComplexity.Complex, CancellationToken.None);

        result.SourceName.Should().Be("web_search");
        result.Results.Should().HaveCount(2);
        result.Results[0].FusedScore.Should().BeGreaterThan(result.Results[1].FusedScore,
            "first result should score higher (rank-decay)");
        result.Results[0].Chunk.Content.Should().Contain("Content 1");
        result.Latency.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RetrieveAsync_EmptyProviderResults_ReturnsEmptySourceResult()
    {
        _provider
            .Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = new WebSearchRetrievalSource(_provider.Object);

        var result = await sut.RetrieveAsync("test", 5, QueryComplexity.Complex, CancellationToken.None);

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public void SourceName_IsWebSearch()
    {
        var sut = new WebSearchRetrievalSource(_provider.Object);
        sut.SourceName.Should().Be("web_search");
    }
}
```

- [ ] **Step 6: Implement `WebSearchRetrievalSource`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/WebSearch/WebSearchRetrievalSource.cs
using System.Diagnostics;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;

namespace Infrastructure.AI.RAG.WebSearch;

/// <summary>
/// Adapts <see cref="IWebSearchProvider"/> to <see cref="IRetrievalSource"/>.
/// Converts web search results to <see cref="RetrievalResult"/> with rank-decay scoring.
/// Registered as keyed DI with key "web_search".
/// </summary>
internal sealed class WebSearchRetrievalSource(IWebSearchProvider webSearchProvider) : IRetrievalSource
{
    private const double DecayFactor = 0.85;

    public string SourceName => "web_search";

    public async Task<SourceRetrievalResult> RetrieveAsync(
        string query, int topK, QueryComplexity complexity, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var webResults = await webSearchProvider.SearchAsync(query, topK, cancellationToken);
        sw.Stop();

        var retrievalResults = webResults
            .Select((wr, index) => ToRetrievalResult(wr, index))
            .ToList();

        return new SourceRetrievalResult
        {
            SourceName = SourceName,
            Results = retrievalResults,
            Latency = sw.Elapsed,
            TokensUsed = 0
        };
    }

    private static RetrievalResult ToRetrievalResult(WebSearchResult wr, int rankIndex)
    {
        var score = Math.Pow(DecayFactor, rankIndex);
        var content = wr.Content ?? wr.Snippet;

        return new RetrievalResult
        {
            Chunk = new DocumentChunk
            {
                Id = $"web-{wr.Url.GetHashCode():x8}",
                DocumentId = wr.Url,
                SectionPath = wr.Title,
                Content = content,
                Tokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                Metadata = new ChunkMetadata
                {
                    SourceFileName = wr.Url,
                    ChunkIndex = rankIndex,
                    TotalChunks = 1
                }
            },
            DenseScore = score,
            SparseScore = 0.0,
            FusedScore = score
        };
    }
}
```

- [ ] **Step 7: Run all web search tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "BingWebSearchProviderTests|WebSearchRetrievalSourceTests"`
Expected: 6 passed.

- [ ] **Step 8: Commit web search source**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/WebSearch/
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/WebSearch/
git commit -m "feat(rag): add web search retrieval source with Bing provider"
```

---

## Task 5: SQL Database — Template Store and Matcher

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/JsonSqlQueryTemplateStore.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SqlQueryTemplateMatcher.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/JsonSqlQueryTemplateStoreTests.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SqlQueryTemplateMatcherTests.cs`

- [ ] **Step 1: Write failing test for `JsonSqlQueryTemplateStore`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/JsonSqlQueryTemplateStoreTests.cs
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.SqlDatabase;

public sealed class JsonSqlQueryTemplateStoreTests
{
    [Fact]
    public async Task GetTemplatesAsync_LoadsFromJsonFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var json = """
            [
                {
                    "Name": "orders_by_date",
                    "Description": "Retrieve orders within a date range",
                    "SqlTemplate": "SELECT * FROM orders WHERE date >= @startDate AND date <= @endDate",
                    "Parameters": ["startDate", "endDate"]
                }
            ]
            """;
            await File.WriteAllTextAsync(tempFile, json);

            var config = CreateConfig(tempFile);
            var sut = new JsonSqlQueryTemplateStore(config);

            var templates = await sut.GetTemplatesAsync(CancellationToken.None);

            templates.Should().HaveCount(1);
            templates[0].Name.Should().Be("orders_by_date");
            templates[0].Parameters.Should().Contain("startDate");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetTemplatesAsync_MissingFile_ReturnsEmpty()
    {
        var config = CreateConfig("/nonexistent/path.json");
        var sut = new JsonSqlQueryTemplateStore(config);

        var templates = await sut.GetTemplatesAsync(CancellationToken.None);

        templates.Should().BeEmpty();
    }

    private static IOptionsMonitor<AppConfig> CreateConfig(string templatesPath)
    {
        var appConfig = new AppConfig();
        appConfig.AI.Rag.SqlDatabase = new SqlDatabaseConfig { TemplatesPath = templatesPath };
        var mock = new Mock<IOptionsMonitor<AppConfig>>();
        mock.Setup(m => m.CurrentValue).Returns(appConfig);
        return mock.Object;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "JsonSqlQueryTemplateStoreTests" --no-build`
Expected: FAIL — `JsonSqlQueryTemplateStore` does not exist.

- [ ] **Step 3: Implement `JsonSqlQueryTemplateStore`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/JsonSqlQueryTemplateStore.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.SqlDatabase;

/// <summary>
/// Loads <see cref="SqlQueryTemplate"/> instances from a JSON file.
/// Templates are cached after first load and refreshed on config change.
/// </summary>
internal sealed class JsonSqlQueryTemplateStore(IOptionsMonitor<AppConfig> configMonitor) : ISqlQueryTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<SqlQueryTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
    {
        var path = configMonitor.CurrentValue.AI.Rag.SqlDatabase.TemplatesPath;

        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<List<SqlQueryTemplate>>(json, JsonOptions) ?? [];
    }
}
```

- [ ] **Step 4: Run template store tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "JsonSqlQueryTemplateStoreTests"`
Expected: 2 passed.

- [ ] **Step 5: Write failing test for `SqlQueryTemplateMatcher`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SqlQueryTemplateMatcherTests.cs
using Domain.AI.RAG.Models;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.SqlDatabase;

public sealed class SqlQueryTemplateMatcherTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    [Fact]
    public async Task MatchAsync_HighConfidence_ReturnsTemplateWithParameters()
    {
        var templates = new List<SqlQueryTemplate>
        {
            new()
            {
                Name = "orders_by_date",
                Description = "Retrieve orders within a date range",
                SqlTemplate = "SELECT * FROM orders WHERE date >= @startDate AND date <= @endDate",
                Parameters = ["startDate", "endDate"]
            }
        };

        var llmResponse = """{"templateName":"orders_by_date","confidence":0.9,"parameters":{"startDate":"2026-01-01","endDate":"2026-12-31"}}""";
        SetupChatResponse(llmResponse);

        var config = CreateConfig(0.7);
        var sut = new SqlQueryTemplateMatcher(_chatClient.Object, config);

        var result = await sut.MatchAsync("Show me orders from 2026", templates, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Value.Template.Name.Should().Be("orders_by_date");
        result.Value.Parameters["startDate"].Should().Be("2026-01-01");
    }

    [Fact]
    public async Task MatchAsync_LowConfidence_ReturnsNull()
    {
        var templates = new List<SqlQueryTemplate>
        {
            new()
            {
                Name = "orders_by_date",
                Description = "Retrieve orders within a date range",
                SqlTemplate = "SELECT * FROM orders WHERE date >= @startDate",
                Parameters = ["startDate"]
            }
        };

        var llmResponse = """{"templateName":"orders_by_date","confidence":0.3,"parameters":{"startDate":"unknown"}}""";
        SetupChatResponse(llmResponse);

        var config = CreateConfig(0.7);
        var sut = new SqlQueryTemplateMatcher(_chatClient.Object, config);

        var result = await sut.MatchAsync("What is the weather?", templates, CancellationToken.None);

        result.Should().BeNull();
    }

    private void SetupChatResponse(string content)
    {
        var chatMessage = new ChatMessage(ChatRole.Assistant, content);
        var chatResponse = new ChatResponse(chatMessage);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    private static IOptionsMonitor<AppConfig> CreateConfig(double threshold)
    {
        var appConfig = new AppConfig();
        appConfig.AI.Rag.SqlDatabase = new SqlDatabaseConfig
        {
            TemplateMatchConfidenceThreshold = threshold
        };
        var mock = new Mock<IOptionsMonitor<AppConfig>>();
        mock.Setup(m => m.CurrentValue).Returns(appConfig);
        return mock.Object;
    }
}
```

- [ ] **Step 6: Implement `SqlQueryTemplateMatcher`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SqlQueryTemplateMatcher.cs
using System.Text.Json;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.SqlDatabase;

/// <summary>
/// Uses an LLM to match a natural language query to the best <see cref="SqlQueryTemplate"/>
/// and extract parameter values. Returns null if confidence is below threshold.
/// </summary>
internal sealed class SqlQueryTemplateMatcher(IChatClient chatClient, IOptionsMonitor<AppConfig> configMonitor)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Result of a successful template match: the matched template and extracted parameter values.
    /// </summary>
    public readonly record struct TemplateMatch(SqlQueryTemplate Template, IReadOnlyDictionary<string, string> Parameters);

    public async Task<TemplateMatch?> MatchAsync(
        string query,
        IReadOnlyList<SqlQueryTemplate> templates,
        CancellationToken cancellationToken)
    {
        if (templates.Count == 0)
            return null;

        var threshold = configMonitor.CurrentValue.AI.Rag.SqlDatabase.TemplateMatchConfidenceThreshold;

        var templateDescriptions = templates
            .Select(t => $"- {t.Name}: {t.Description} (params: {string.Join(", ", t.Parameters)})")
            .Aggregate((a, b) => $"{a}\n{b}");

        var systemPrompt = $"""
            You are a SQL template matcher. Given a natural language query and available templates,
            select the best matching template and extract parameter values.
            
            Available templates:
            {templateDescriptions}
            
            Respond with JSON only: {{"templateName":"...","confidence":0.0-1.0,"parameters":{{"param":"value"}}}}
            If no template matches, return {{"templateName":"none","confidence":0.0,"parameters":{{}}}}
            """;

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, query)
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var responseText = response.Message.Text ?? "";

        var parsed = JsonSerializer.Deserialize<MatchResponse>(responseText, JsonOptions);
        if (parsed is null || parsed.Confidence < threshold || parsed.TemplateName == "none")
            return null;

        var matchedTemplate = templates.FirstOrDefault(t =>
            t.Name.Equals(parsed.TemplateName, StringComparison.OrdinalIgnoreCase));

        if (matchedTemplate is null)
            return null;

        return new TemplateMatch(matchedTemplate, parsed.Parameters ?? new Dictionary<string, string>());
    }

    private sealed record MatchResponse(string TemplateName, double Confidence, Dictionary<string, string>? Parameters);
}
```

- [ ] **Step 7: Run all SQL template tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "JsonSqlQueryTemplateStoreTests|SqlQueryTemplateMatcherTests"`
Expected: 4 passed.

- [ ] **Step 8: Commit SQL template store and matcher**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/JsonSqlQueryTemplateStore.cs
git add src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SqlQueryTemplateMatcher.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/
git commit -m "feat(rag): add SQL query template store and LLM-based matcher"
```

---

## Task 6: SQL Database — Text-to-SQL Generator and Safe Executor

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/TextToSqlGenerator.cs`
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SafeSqlQueryExecutor.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/TextToSqlGeneratorTests.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SafeSqlQueryExecutorTests.cs`

- [ ] **Step 1: Write failing test for `TextToSqlGenerator`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/TextToSqlGeneratorTests.cs
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;

namespace Infrastructure.AI.RAG.Tests.SqlDatabase;

public sealed class TextToSqlGeneratorTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    [Fact]
    public async Task GenerateAsync_ReturnsSelectStatement()
    {
        var llmResponse = "SELECT name, email FROM users WHERE active = 1";
        SetupChatResponse(llmResponse);

        var sut = new TextToSqlGenerator(_chatClient.Object);
        var schema = "CREATE TABLE users (id INT, name TEXT, email TEXT, active INT)";

        var sql = await sut.GenerateAsync("Show me all active users", schema, CancellationToken.None);

        sql.Should().NotBeNullOrEmpty();
        sql.Should().StartWith("SELECT", "generated SQL must be a SELECT statement");
    }

    [Fact]
    public async Task GenerateAsync_LlmReturnsMutation_ReturnsNull()
    {
        var llmResponse = "DELETE FROM users WHERE active = 0";
        SetupChatResponse(llmResponse);

        var sut = new TextToSqlGenerator(_chatClient.Object);

        var sql = await sut.GenerateAsync("Delete inactive users", "schema", CancellationToken.None);

        sql.Should().BeNull("mutations must be rejected at generation time");
    }

    private void SetupChatResponse(string content)
    {
        var chatMessage = new ChatMessage(ChatRole.Assistant, content);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(chatMessage));
    }
}
```

- [ ] **Step 2: Implement `TextToSqlGenerator`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/TextToSqlGenerator.cs
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Infrastructure.AI.RAG.SqlDatabase;

/// <summary>
/// LLM fallback: generates a SELECT-only SQL query from natural language and a database schema.
/// Rejects any generated SQL containing mutation keywords as a defense-in-depth measure.
/// </summary>
internal sealed partial class TextToSqlGenerator(IChatClient chatClient)
{
    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|GRANT|REVOKE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MutationPattern();

    public async Task<string?> GenerateAsync(
        string naturalLanguageQuery, string databaseSchema, CancellationToken cancellationToken)
    {
        var systemPrompt = $"""
            You are a SQL query generator. Generate a single SELECT query based on the user's question.

            Rules:
            - ONLY generate SELECT statements. Never INSERT, UPDATE, DELETE, DROP, ALTER, TRUNCATE, or any DDL/DML.
            - Use the schema below to determine valid tables and columns.
            - Include LIMIT 100 unless the user specifies a different limit.
            - Do not use subqueries without LIMIT.
            - Respond with ONLY the SQL query, no explanation.

            Database schema:
            {databaseSchema}
            """;

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, naturalLanguageQuery)
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var sql = response.Message.Text?.Trim();

        if (string.IsNullOrEmpty(sql))
            return null;

        if (MutationPattern().IsMatch(sql))
            return null;

        return sql;
    }
}
```

- [ ] **Step 3: Run text-to-SQL tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "TextToSqlGeneratorTests"`
Expected: 2 passed.

- [ ] **Step 4: Write failing tests for `SafeSqlQueryExecutor`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SafeSqlQueryExecutorTests.cs
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.SqlDatabase;

public sealed class SafeSqlQueryExecutorTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SafeSqlQueryExecutorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL);
            INSERT INTO products VALUES (1, 'Widget', 9.99);
            INSERT INTO products VALUES (2, 'Gadget', 19.99);
            INSERT INTO products VALUES (3, 'Doohickey', 4.99);
        """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task ExecuteAsync_ValidSelect_ReturnsRows()
    {
        var sut = CreateExecutor();

        var result = await sut.ExecuteAsync("SELECT name, price FROM products", null, CancellationToken.None);

        result.Rows.Should().HaveCount(3);
        result.WasTemplateMatch.Should().BeFalse();
        result.Rows[0]["name"].Should().Be("Widget");
    }

    [Theory]
    [InlineData("INSERT INTO products VALUES (4, 'Bad', 0)")]
    [InlineData("UPDATE products SET price = 0")]
    [InlineData("DELETE FROM products WHERE id = 1")]
    [InlineData("DROP TABLE products")]
    [InlineData("ALTER TABLE products ADD COLUMN evil TEXT")]
    [InlineData("TRUNCATE TABLE products")]
    public async Task ExecuteAsync_MutationSql_ThrowsInvalidOperationException(string sql)
    {
        var sut = CreateExecutor();

        var act = () => sut.ExecuteAsync(sql, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only*");
    }

    [Fact]
    public async Task ExecuteAsync_RespectsRowLimit()
    {
        var sut = CreateExecutor(maxRows: 2);

        var result = await sut.ExecuteAsync("SELECT * FROM products", null, CancellationToken.None);

        result.Rows.Should().HaveCount(2, "row limit should cap results");
    }

    [Fact]
    public async Task ExecuteAsync_WithParameters_BindsCorrectly()
    {
        var sut = CreateExecutor();
        var parameters = new Dictionary<string, object?> { ["minPrice"] = 5.0 };

        var result = await sut.ExecuteAsync(
            "SELECT name FROM products WHERE price > @minPrice", parameters, CancellationToken.None);

        result.Rows.Should().HaveCount(2);
    }

    private SafeSqlQueryExecutor CreateExecutor(int maxRows = 100)
    {
        var appConfig = new AppConfig();
        appConfig.AI.Rag.SqlDatabase = new SqlDatabaseConfig
        {
            MaxRows = maxRows,
            QueryTimeoutSeconds = 5
        };
        var configMock = new Mock<IOptionsMonitor<AppConfig>>();
        configMock.Setup(m => m.CurrentValue).Returns(appConfig);

        return new SafeSqlQueryExecutor(
            _connection,
            configMock.Object,
            NullLogger<SafeSqlQueryExecutor>.Instance);
    }

    public void Dispose() => _connection.Dispose();
}
```

- [ ] **Step 5: Implement `SafeSqlQueryExecutor`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SafeSqlQueryExecutor.cs
using System.Data.Common;
using System.Text.RegularExpressions;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.SqlDatabase;

/// <summary>
/// Executes SQL queries with safety guardrails: read-only enforcement, row limits, and query timeout.
/// Rejects any SQL containing mutation keywords before execution.
/// </summary>
internal sealed partial class SafeSqlQueryExecutor(
    DbConnection connection,
    IOptionsMonitor<AppConfig> configMonitor,
    ILogger<SafeSqlQueryExecutor> logger) : ISqlQueryExecutor
{
    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|GRANT|REVOKE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MutationPattern();

    public async Task<SqlRetrievalResult> ExecuteAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        if (MutationPattern().IsMatch(sql))
            throw new InvalidOperationException(
                $"SQL query rejected: only read-only SELECT statements are allowed. Query contained a mutation keyword.");

        var config = configMonitor.CurrentValue.AI.Rag.SqlDatabase;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = config.QueryTimeoutSeconds;

        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
            {
                var param = cmd.CreateParameter();
                param.ParameterName = key;
                param.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(param);
            }
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken) && rows.Count < config.MaxRows)
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        logger.LogDebug("SQL query returned {RowCount} rows (limit: {MaxRows})", rows.Count, config.MaxRows);

        return new SqlRetrievalResult
        {
            Query = sql,
            WasTemplateMatch = false,
            Rows = rows
        };
    }
}
```

- [ ] **Step 6: Run safe executor tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "SafeSqlQueryExecutorTests"`
Expected: All passed (4 tests: valid select, 6 mutation rejections via Theory, row limit, parameters).

- [ ] **Step 7: Commit text-to-SQL and safe executor**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/TextToSqlGenerator.cs
git add src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SafeSqlQueryExecutor.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/TextToSqlGeneratorTests.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SafeSqlQueryExecutorTests.cs
git commit -m "feat(rag): add text-to-SQL generator and safe query executor with read-only enforcement"
```

---

## Task 7: SQL Database — Retrieval Source Adapter

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SqlDatabaseRetrievalSource.cs`
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SqlDatabaseRetrievalSourceTests.cs`

- [ ] **Step 1: Write failing test for `SqlDatabaseRetrievalSource`**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SqlDatabaseRetrievalSourceTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.SqlDatabase;

public sealed class SqlDatabaseRetrievalSourceTests
{
    private readonly Mock<ISqlQueryTemplateStore> _templateStore = new();
    private readonly Mock<ISqlQueryExecutor> _executor = new();
    private readonly Mock<IChatClient> _chatClient = new();

    [Fact]
    public async Task RetrieveAsync_TemplateMatch_ExecutesTemplateAndConvertsToResults()
    {
        var template = new SqlQueryTemplate
        {
            Name = "user_by_name",
            Description = "Find user by name",
            SqlTemplate = "SELECT * FROM users WHERE name = @name",
            Parameters = ["name"]
        };
        _templateStore
            .Setup(s => s.GetTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([template]);

        _executor
            .Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlRetrievalResult
            {
                Query = "SELECT * FROM users WHERE name = @name",
                WasTemplateMatch = true,
                Rows = [new Dictionary<string, object?> { ["id"] = 1L, ["name"] = "Alice" }]
            });

        var matcherResponse = """{"templateName":"user_by_name","confidence":0.9,"parameters":{"name":"Alice"}}""";
        SetupChatResponse(matcherResponse);

        var sut = CreateSut(allowLlmFallback: true);

        var result = await sut.RetrieveAsync("Find user Alice", 10, QueryComplexity.Moderate, CancellationToken.None);

        result.SourceName.Should().Be("sql_database");
        result.Results.Should().HaveCount(1);
        result.Results[0].Chunk.Content.Should().Contain("Alice");
    }

    [Fact]
    public async Task RetrieveAsync_NoTemplateMatch_FallsBackToLlm()
    {
        _templateStore
            .Setup(s => s.GetTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var fallbackSql = "SELECT * FROM products WHERE price < 10";
        SetupChatResponse(fallbackSql);

        _executor
            .Setup(e => e.ExecuteAsync(fallbackSql, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlRetrievalResult
            {
                Query = fallbackSql,
                WasTemplateMatch = false,
                Rows = [new Dictionary<string, object?> { ["name"] = "Widget", ["price"] = 9.99 }]
            });

        var sut = CreateSut(allowLlmFallback: true);

        var result = await sut.RetrieveAsync("Cheap products", 10, QueryComplexity.Moderate, CancellationToken.None);

        result.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task RetrieveAsync_LlmFallbackDisabled_ReturnsEmpty()
    {
        _templateStore
            .Setup(s => s.GetTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(allowLlmFallback: false);

        var result = await sut.RetrieveAsync("Cheap products", 10, QueryComplexity.Moderate, CancellationToken.None);

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public void SourceName_IsSqlDatabase()
    {
        var sut = CreateSut(allowLlmFallback: true);
        sut.SourceName.Should().Be("sql_database");
    }

    private SqlDatabaseRetrievalSource CreateSut(bool allowLlmFallback)
    {
        var appConfig = new AppConfig();
        appConfig.AI.Rag.SqlDatabase = new SqlDatabaseConfig
        {
            AllowLlmFallback = allowLlmFallback,
            TemplateMatchConfidenceThreshold = 0.7
        };
        var configMock = new Mock<IOptionsMonitor<AppConfig>>();
        configMock.Setup(m => m.CurrentValue).Returns(appConfig);

        return new SqlDatabaseRetrievalSource(
            _templateStore.Object,
            _executor.Object,
            new SqlQueryTemplateMatcher(_chatClient.Object, configMock.Object),
            new TextToSqlGenerator(_chatClient.Object),
            configMock.Object,
            NullLogger<SqlDatabaseRetrievalSource>.Instance);
    }

    private void SetupChatResponse(string content)
    {
        var chatMessage = new ChatMessage(ChatRole.Assistant, content);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(chatMessage));
    }
}
```

Note: add `using Microsoft.Extensions.AI;` for `IChatClient`, `ChatMessage`, `ChatRole`, `ChatResponse`.

- [ ] **Step 2: Implement `SqlDatabaseRetrievalSource`**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SqlDatabaseRetrievalSource.cs
using System.Diagnostics;
using System.Text.Json;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.SqlDatabase;

/// <summary>
/// Two-tier SQL retrieval source: tries template matching first, falls back to LLM text-to-SQL
/// if <see cref="SqlDatabaseConfig.AllowLlmFallback"/> is enabled.
/// Registered as keyed DI with key "sql_database".
/// </summary>
internal sealed class SqlDatabaseRetrievalSource(
    ISqlQueryTemplateStore templateStore,
    ISqlQueryExecutor executor,
    SqlQueryTemplateMatcher templateMatcher,
    TextToSqlGenerator textToSqlGenerator,
    IOptionsMonitor<AppConfig> configMonitor,
    ILogger<SqlDatabaseRetrievalSource> logger) : IRetrievalSource
{
    public string SourceName => "sql_database";

    public async Task<SourceRetrievalResult> RetrieveAsync(
        string query, int topK, QueryComplexity complexity, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var config = configMonitor.CurrentValue.AI.Rag.SqlDatabase;

        var templates = await templateStore.GetTemplatesAsync(cancellationToken);

        // Tier 1: Template matching
        if (templates.Count > 0)
        {
            var match = await templateMatcher.MatchAsync(query, templates, cancellationToken);
            if (match is not null)
            {
                var paramDict = match.Value.Parameters.ToDictionary(
                    kvp => kvp.Key, kvp => (object?)kvp.Value);
                var sqlResult = await executor.ExecuteAsync(
                    match.Value.Template.SqlTemplate, paramDict, cancellationToken);
                sw.Stop();

                return ToSourceResult(sqlResult with { WasTemplateMatch = true }, sw.Elapsed);
            }
        }

        // Tier 2: LLM fallback
        if (!config.AllowLlmFallback)
        {
            sw.Stop();
            return new SourceRetrievalResult
            {
                SourceName = SourceName, Results = [], Latency = sw.Elapsed, TokensUsed = 0
            };
        }

        var generatedSql = await textToSqlGenerator.GenerateAsync(query, "", cancellationToken);
        if (generatedSql is null)
        {
            sw.Stop();
            logger.LogWarning("Text-to-SQL generation returned null for query '{Query}'", query);
            return new SourceRetrievalResult
            {
                SourceName = SourceName, Results = [], Latency = sw.Elapsed, TokensUsed = 0
            };
        }

        var fallbackResult = await executor.ExecuteAsync(generatedSql, null, cancellationToken);
        sw.Stop();

        return ToSourceResult(fallbackResult, sw.Elapsed);
    }

    private SourceRetrievalResult ToSourceResult(SqlRetrievalResult sqlResult, TimeSpan latency)
    {
        var results = sqlResult.Rows
            .Select((row, index) => RowToRetrievalResult(row, index, sqlResult.Query, sqlResult.WasTemplateMatch))
            .ToList();

        return new SourceRetrievalResult
        {
            SourceName = SourceName,
            Results = results,
            Latency = latency,
            TokensUsed = 0
        };
    }

    private static RetrievalResult RowToRetrievalResult(
        IReadOnlyDictionary<string, object?> row, int index, string query, bool wasTemplate)
    {
        var content = JsonSerializer.Serialize(row);
        var score = 1.0 / (1 + index);

        return new RetrievalResult
        {
            Chunk = new DocumentChunk
            {
                Id = $"sql-{query.GetHashCode():x8}-{index}",
                DocumentId = $"sql:{(wasTemplate ? "template" : "generated")}",
                SectionPath = "SQL Result",
                Content = content,
                Tokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                Metadata = new ChunkMetadata
                {
                    SourceFileName = query,
                    ChunkIndex = index,
                    TotalChunks = 1
                }
            },
            DenseScore = score,
            SparseScore = 0.0,
            FusedScore = score
        };
    }
}
```

- [ ] **Step 3: Run SQL adapter tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests --filter "SqlDatabaseRetrievalSourceTests"`
Expected: 4 passed.

- [ ] **Step 4: Commit SQL retrieval source adapter**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/SqlDatabase/SqlDatabaseRetrievalSource.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/SqlDatabase/SqlDatabaseRetrievalSourceTests.cs
git commit -m "feat(rag): add SQL database retrieval source with template-first and LLM fallback"
```

---

## Task 8: DI Registration, Config Wiring, and Integration Tests

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs`
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Infrastructure.AI.RAG.csproj` (if new packages needed)
- Test: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceIntegrationTests.cs`

- [ ] **Step 1: Update `DependencyInjection.cs` — refactor `AddRagMultiSource` and add new methods**

In `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs`, replace the existing `AddRagMultiSource` method and add `AddRagWebSearch` and `AddRagSqlDatabase`:

```csharp
private static void AddRagMultiSource(IServiceCollection services, AppConfig appConfig)
{
    services.AddSingleton<IRetrievalCostTracker, RetrievalCostTracker>();

    // Adapter: existing IHybridRetriever → IRetrievalSource
    services.AddKeyedSingleton<IRetrievalSource>("vector", (sp, _) =>
        new VectorRetrievalSource(sp.GetRequiredService<IHybridRetriever>()));

    // Adapter: existing IGraphRagService → IRetrievalSource
    services.AddKeyedSingleton<IRetrievalSource>("graph", (sp, _) =>
        new GraphRetrievalSource(sp.GetRequiredService<IGraphRagService>()));

    // Orchestrator resolves IRetrievalSource by key from the container
    services.AddSingleton<IMultiSourceOrchestrator>(sp =>
        new MultiSourceOrchestrator(
            sp,
            sp.GetRequiredService<IRetrievalCostTracker>(),
            sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
            sp.GetRequiredService<ILogger<MultiSourceOrchestrator>>()));
}

private static void AddRagWebSearch(IServiceCollection services, AppConfig appConfig)
{
    var config = appConfig.AI?.Rag?.WebSearch;
    if (config is null) return;

    var providerKey = config.Provider ?? "bing";
    services.AddKeyedSingleton<IWebSearchProvider>(providerKey, (sp, _) =>
        new BingWebSearchProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("BingSearch"),
            sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
            sp.GetRequiredService<ILogger<BingWebSearchProvider>>()));

    services.AddKeyedSingleton<IRetrievalSource>("web_search", (sp, _) =>
        new WebSearchRetrievalSource(
            sp.GetRequiredKeyedService<IWebSearchProvider>(providerKey)));
}

private static void AddRagSqlDatabase(IServiceCollection services, AppConfig appConfig)
{
    var config = appConfig.AI?.Rag?.SqlDatabase;
    if (config is null || !config.Enabled) return;

    services.AddSingleton<ISqlQueryTemplateStore, JsonSqlQueryTemplateStore>();
    services.AddSingleton<ISqlQueryExecutor, SafeSqlQueryExecutor>();
    services.AddSingleton<SqlQueryTemplateMatcher>();
    services.AddSingleton<TextToSqlGenerator>();

    services.AddKeyedSingleton<IRetrievalSource>("sql_database", (sp, _) =>
        new SqlDatabaseRetrievalSource(
            sp.GetRequiredService<ISqlQueryTemplateStore>(),
            sp.GetRequiredService<ISqlQueryExecutor>(),
            sp.GetRequiredService<SqlQueryTemplateMatcher>(),
            sp.GetRequiredService<TextToSqlGenerator>(),
            sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
            sp.GetRequiredService<ILogger<SqlDatabaseRetrievalSource>>()));
}
```

Add calls to the new methods in `AddRagDependencies`:

```csharp
public static IServiceCollection AddRagDependencies(this IServiceCollection services, AppConfig appConfig)
{
    // ... existing calls ...
    AddRagMultiSource(services, appConfig);     // refactored
    AddRagWebSearch(services, appConfig);        // NEW
    AddRagSqlDatabase(services, appConfig);      // NEW
    return services;
}
```

Add required usings at the top of the file:

```csharp
using Infrastructure.AI.RAG.Orchestration;
using Infrastructure.AI.RAG.WebSearch;
using Infrastructure.AI.RAG.SqlDatabase;
```

- [ ] **Step 2: Write integration test for multi-source fan-out with keyed DI**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceIntegrationTests.cs
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Enums;
using Domain.AI.RAG.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.Orchestration;

public sealed class MultiSourceIntegrationTests
{
    [Fact]
    public async Task FanOut_QueriesAllEnabledSources_MergesResults()
    {
        var vectorSource = CreateMockSource("vector", [CreateResult("v1", 0.9)]);
        var graphSource = CreateMockSource("graph", [CreateResult("g1", 0.8)]);
        var webSource = CreateMockSource("web_search", [CreateResult("w1", 0.7)]);

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IRetrievalSource>("vector", vectorSource.Object);
        services.AddKeyedSingleton<IRetrievalSource>("graph", graphSource.Object);
        services.AddKeyedSingleton<IRetrievalSource>("web_search", webSource.Object);

        var config = CreateConfig(
            enabledSources: ["vector", "graph", "web_search"],
            sourcesByComplexity: new()
            {
                ["Complex"] = ["vector", "graph", "web_search"]
            });

        var sp = services.BuildServiceProvider();
        var sut = new MultiSourceOrchestrator(
            sp, Mock.Of<IRetrievalCostTracker>(), config, NullLogger<MultiSourceOrchestrator>.Instance);

        var results = await sut.RetrieveFromAllSourcesAsync("complex query", 10, QueryComplexity.Complex);

        results.Should().HaveCount(3);
        results.Should().BeInDescendingOrder(r => r.FusedScore);
    }

    [Fact]
    public async Task FanOut_DeduplicatesByChunkId_KeepsHighestScore()
    {
        var source1 = CreateMockSource("vector", [CreateResult("shared-id", 0.9)]);
        var source2 = CreateMockSource("graph", [CreateResult("shared-id", 0.7)]);

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IRetrievalSource>("vector", source1.Object);
        services.AddKeyedSingleton<IRetrievalSource>("graph", source2.Object);

        var config = CreateConfig(
            enabledSources: ["vector", "graph"],
            sourcesByComplexity: new() { ["Moderate"] = ["vector", "graph"] });

        var sp = services.BuildServiceProvider();
        var sut = new MultiSourceOrchestrator(
            sp, Mock.Of<IRetrievalCostTracker>(), config, NullLogger<MultiSourceOrchestrator>.Instance);

        var results = await sut.RetrieveFromAllSourcesAsync("test", 10, QueryComplexity.Moderate);

        results.Should().HaveCount(1);
        results[0].FusedScore.Should().Be(0.9, "should keep the higher-scoring duplicate");
    }

    private static Mock<IRetrievalSource> CreateMockSource(string name, IReadOnlyList<RetrievalResult> results)
    {
        var mock = new Mock<IRetrievalSource>();
        mock.Setup(s => s.SourceName).Returns(name);
        mock.Setup(s => s.RetrieveAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<QueryComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceRetrievalResult
            {
                SourceName = name, Results = results, Latency = TimeSpan.FromMilliseconds(50), TokensUsed = 0
            });
        return mock;
    }

    private static RetrievalResult CreateResult(string chunkId, double score) => new()
    {
        Chunk = new DocumentChunk
        {
            Id = chunkId,
            DocumentId = "doc-1",
            SectionPath = "Test",
            Content = $"Content for {chunkId}",
            Tokens = 5,
            Metadata = new ChunkMetadata { SourceFileName = "test.md", ChunkIndex = 0, TotalChunks = 1 }
        },
        DenseScore = score,
        SparseScore = 0.0,
        FusedScore = score
    };

    private static IOptionsMonitor<AppConfig> CreateConfig(
        List<string> enabledSources,
        Dictionary<string, List<string>> sourcesByComplexity)
    {
        var appConfig = new AppConfig();
        appConfig.AI.Rag.MultiSource.Enabled = true;
        appConfig.AI.Rag.MultiSource.EnabledSources = enabledSources;
        appConfig.AI.Rag.MultiSource.SourcesByComplexity = sourcesByComplexity;
        appConfig.AI.Rag.MultiSource.SourceTimeout = TimeSpan.FromSeconds(5);
        appConfig.AI.Rag.MultiSource.MaxParallelSources = 5;

        var mock = new Mock<IOptionsMonitor<AppConfig>>();
        mock.Setup(m => m.CurrentValue).Returns(appConfig);
        return mock.Object;
    }
}
```

- [ ] **Step 3: Build the full solution**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Run all RAG tests**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.RAG.Tests`
Expected: All tests pass including new and existing.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass across all projects.

- [ ] **Step 6: Commit DI wiring and integration tests**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceIntegrationTests.cs
git commit -m "feat(rag): wire web search and SQL database sources into DI and add integration tests"
```

---

## Task 9: Fix Existing Test Regressions and Final Verification

**Files:**
- Modify: any existing test files that break due to the `MultiSourceOrchestrator` constructor change

- [ ] **Step 1: Identify and fix any tests using the old constructor**

Search for existing tests that construct `MultiSourceOrchestrator` directly with the old `(IHybridRetriever, IGraphRagService, ...)` signature. Update them to use the new `(IServiceProvider, ...)` signature by building a `ServiceCollection` with keyed mocks, same pattern as `MultiSourceOrchestratorRefactorTests`.

Key files to check:
- `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/MultiSourceOrchestratorTests.cs`
- `src/Content/Tests/Infrastructure.AI.RAG.Tests/Orchestration/FullAutonomyIntegrationTests.cs`

For each test that constructs the old orchestrator, replace:

```csharp
// OLD
var sut = new MultiSourceOrchestrator(
    mockRetriever.Object, mockGraphRag.Object, costTracker.Object, config, logger);

// NEW
var services = new ServiceCollection();
services.AddKeyedSingleton<IRetrievalSource>("vector", mockVectorSource.Object);
services.AddKeyedSingleton<IRetrievalSource>("graph", mockGraphSource.Object);
var sp = services.BuildServiceProvider();
var sut = new MultiSourceOrchestrator(sp, costTracker.Object, config, logger);
```

- [ ] **Step 2: Run the full solution build**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass.

- [ ] **Step 4: Commit regression fixes**

```bash
git add -u
git commit -m "fix(rag): update existing tests for refactored MultiSourceOrchestrator constructor"
```

- [ ] **Step 5: Final verification — build + test from clean state**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx`
Expected: Build succeeded, all tests pass.
