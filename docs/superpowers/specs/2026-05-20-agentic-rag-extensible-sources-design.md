# Agentic RAG — Extensible Retrieval Sources

**Date:** 2026-05-20
**Status:** Approved
**Scope:** Introduce `IRetrievalSource` abstraction, add Web Search and SQL Database retrieval sources, refactor `MultiSourceOrchestrator` to keyed DI resolution.

---

## Problem

The `MultiSourceOrchestrator` uses a hardcoded switch statement to dispatch to retrieval sources. Adding a new source requires modifying the orchestrator constructor, the switch, and the complexity routing logic. The "Agentic RAG" pattern (image reference: Brij Pandey's 2026 architecture guide) shows a planner agent routing to vector search, web search, and SQL database tools — our orchestrator only covers vector and graph today.

## Decision

**Approach A — Keyed DI `IRetrievalSource`** (selected over Source Registry and Minimal Switch Extension).

Rationale: follows the existing codebase pattern used by rerankers (`IReranker`), chunking strategies (`IChunkingService`), and tools (`ITool`). Keyed DI with string identifiers gives full extensibility with minimal abstraction overhead.

## Design

### 1. Core Abstraction

**New interface** — `Application.AI.Common/Interfaces/RAG/IRetrievalSource.cs`:

```csharp
public interface IRetrievalSource
{
    string SourceName { get; }

    Task<SourceRetrievalResult> RetrieveAsync(
        string query,
        int topK,
        QueryComplexity complexity,
        CancellationToken cancellationToken);
}
```

All sources implement this contract. The orchestrator resolves sources by key from DI.

**Adapters for existing sources:**

- `VectorRetrievalSource` — wraps `IHybridRetriever`, keyed as `"vector"`
- `GraphRetrievalSource` — wraps `IGraphRagService`, keyed as `"graph"`

### 2. MultiSourceOrchestrator Refactor

**Before:**
```csharp
public MultiSourceOrchestrator(
    IHybridRetriever hybridRetriever,
    IGraphRagService graphRagService,
    IRetrievalCostTracker costTracker,
    IOptionsMonitor<AppConfig> config,
    ILogger<MultiSourceOrchestrator> logger)
```

**After:**
```csharp
public MultiSourceOrchestrator(
    IServiceProvider serviceProvider,          // resolves IRetrievalSource by key
    IRetrievalCostTracker costTracker,
    IOptionsMonitor<AppConfig> config,
    ILogger<MultiSourceOrchestrator> logger)
```

The switch statement in `ExecuteSourceWithTimeoutAsync` is replaced by keyed DI resolution:

```csharp
var source = serviceProvider.GetKeyedService<IRetrievalSource>(sourceName);
if (source is null) { /* log warning, skip */ }
var result = await source.RetrieveAsync(query, topK, complexity, ct);
```

### 3. Complexity Routing Update

The `RetrievalDecisionGate` complexity-to-source mapping becomes config-driven:

```json
{
  "ComplexityRouting": {
    "SourcesByComplexity": {
      "Trivial": ["vector"],
      "Simple": ["vector"],
      "Moderate": ["vector", "graph"],
      "Complex": ["vector", "graph", "web_search", "sql_database"]
    }
  }
}
```

Filtered by `MultiSourceConfig.EnabledSources` at runtime — a source must appear in both the complexity mapping AND the enabled list to be queried.

### 4. Web Search Source

**Provider interface** — `Application.AI.Common/Interfaces/RAG/IWebSearchProvider.cs`:

```csharp
public interface IWebSearchProvider
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken);
}
```

**Domain model** — `Domain.AI/RAG/Models/WebSearchResult.cs`:

```csharp
public sealed record WebSearchResult
{
    public required string Title { get; init; }
    public required string Snippet { get; init; }
    public required string Url { get; init; }
    public required string? Content { get; init; }
}
```

**Implementations** (`Infrastructure.AI.RAG/WebSearch/`):

| Class | Purpose |
|-------|---------|
| `BingWebSearchProvider` | Calls Bing Search API v7 via HttpClient. API key from User Secrets / Key Vault. |
| `WebSearchRetrievalSource` | Adapter: IWebSearchProvider → IRetrievalSource. Converts WebSearchResult to RetrievalResult with rank-decay scoring and URL citation. |

**Config** — `Domain.Common/Config/AI/RAG/WebSearchConfig.cs`:

```csharp
public sealed class WebSearchConfig
{
    public string Provider { get; set; } = "bing";
    public string? Endpoint { get; set; }
    public int MaxResults { get; set; } = 5;
    public string Market { get; set; } = "en-US";
    public string SafeSearch { get; set; } = "Moderate";
}
```

**Future providers:** Implement `IWebSearchProvider`, register as keyed service (e.g., `"tavily"`, `"google"`), set `WebSearchConfig.Provider`. No orchestrator changes.

### 5. SQL Database Source

**Two-tier architecture:** Template matching first, LLM text-to-SQL fallback.

**Domain models** — `Domain.AI/RAG/Models/`:

```csharp
public sealed record SqlQueryTemplate
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string SqlTemplate { get; init; }
    public required IReadOnlyList<string> Parameters { get; init; }
}

public sealed record SqlRetrievalResult
{
    public required string Query { get; init; }
    public required bool WasTemplateMatch { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }
}
```

**Interfaces** — `Application.AI.Common/Interfaces/RAG/`:

```csharp
public interface ISqlQueryTemplateStore
{
    Task<IReadOnlyList<SqlQueryTemplate>> GetTemplatesAsync(CancellationToken ct);
}

public interface ISqlQueryExecutor
{
    Task<SqlRetrievalResult> ExecuteAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken ct);
}
```

**Implementations** (`Infrastructure.AI.RAG/SqlDatabase/`):

| Class | Purpose |
|-------|---------|
| `JsonSqlQueryTemplateStore` | Loads templates from `sql-templates.json`. No code changes to add templates. |
| `SqlQueryTemplateMatcher` | LLM picks best template + extracts parameters. Returns null if confidence < threshold. |
| `TextToSqlGenerator` | LLM fallback. Generates SELECT-only SQL from schema + natural language query. |
| `SafeSqlQueryExecutor` | Security boundary. Validates read-only (rejects INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE), enforces row limit + query timeout. Uses `DbConnection` abstraction. |
| `SqlDatabaseRetrievalSource` | Adapter: template match → execute, or LLM fallback → validate → execute. Converts rows to RetrievalResult with table/query citation. |

**Config** — `Domain.Common/Config/AI/RAG/SqlDatabaseConfig.cs`:

```csharp
public sealed class SqlDatabaseConfig
{
    public bool Enabled { get; set; } = false;       // opt-in only
    public string TemplatesPath { get; set; } = "sql-templates.json";
    public bool AllowLlmFallback { get; set; } = true;
    public int MaxRows { get; set; } = 100;
    public int QueryTimeoutSeconds { get; set; } = 5;
    public double TemplateMatchConfidenceThreshold { get; set; } = 0.7;
}
```

**Security guardrails:**

| Guardrail | Implementation |
|-----------|---------------|
| Read-only enforcement | `SafeSqlQueryExecutor` rejects any SQL containing mutation keywords before execution |
| Row limit | `TOP {MaxRows}` / `LIMIT {MaxRows}` injected if not present |
| Query timeout | `DbCommand.CommandTimeout` set from config |
| Connection string isolation | User Secrets (dev) / Key Vault (prod), never in appsettings |
| LLM fallback kill switch | `AllowLlmFallback = false` disables text-to-SQL entirely |
| Template-first | Reduces exposure to LLM-generated SQL on known query patterns |

### 6. DI Registration

```csharp
// Infrastructure.AI.RAG/DependencyInjection.cs

private static void AddRagMultiSource(IServiceCollection services, AppConfig appConfig)
{
    // Adapters for existing sources
    services.AddKeyedSingleton<IRetrievalSource>("vector", (sp, _) =>
        new VectorRetrievalSource(sp.GetRequiredService<IHybridRetriever>(), ...));
    services.AddKeyedSingleton<IRetrievalSource>("graph", (sp, _) =>
        new GraphRetrievalSource(sp.GetRequiredService<IGraphRagService>(), ...));

    // Orchestrator now resolves sources by key
    services.AddSingleton<IMultiSourceOrchestrator>(sp =>
        new MultiSourceOrchestrator(sp, ...));

    services.AddSingleton<IRetrievalCostTracker, RetrievalCostTracker>();
}

private static void AddRagWebSearch(IServiceCollection services, AppConfig appConfig)
{
    var config = appConfig.AI.Rag.WebSearch;
    services.AddKeyedSingleton<IWebSearchProvider>(config.Provider, (sp, _) => config.Provider switch
    {
        "bing" => new BingWebSearchProvider(...),
        _ => new BingWebSearchProvider(...)
    });
    services.AddKeyedSingleton<IRetrievalSource>("web_search", (sp, _) =>
        new WebSearchRetrievalSource(
            sp.GetRequiredKeyedService<IWebSearchProvider>(config.Provider), ...));
}

private static void AddRagSqlDatabase(IServiceCollection services, AppConfig appConfig)
{
    if (!appConfig.AI.Rag.SqlDatabase.Enabled) return;

    services.AddSingleton<ISqlQueryTemplateStore, JsonSqlQueryTemplateStore>();
    services.AddSingleton<ISqlQueryExecutor, SafeSqlQueryExecutor>();
    services.AddKeyedSingleton<IRetrievalSource>("sql_database", (sp, _) =>
        new SqlDatabaseRetrievalSource(...));
}
```

### 7. Testing Strategy

| Test Category | Scope | Approach |
|---------------|-------|----------|
| VectorRetrievalSource adapter | Unit | Mock IHybridRetriever, verify RetrievalResult mapping |
| GraphRetrievalSource adapter | Unit | Mock IGraphRagService, verify RetrievalResult mapping |
| BingWebSearchProvider | Unit | Mock HttpClient, verify Bing API response parsing |
| WebSearchRetrievalSource | Unit | Mock IWebSearchProvider, verify rank-decay scoring |
| SqlQueryTemplateMatcher | Unit | Mock IChatClient, verify template selection + param extraction |
| TextToSqlGenerator | Unit | Mock IChatClient, verify generated SQL structure |
| SafeSqlQueryExecutor | Unit | Verify mutation rejection (INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE), row limit injection, timeout |
| SqlDatabaseRetrievalSource | Unit | Mock template store + executor, verify template-first then fallback flow |
| MultiSourceOrchestrator | Integration | Register multiple IRetrievalSource mocks via keyed DI, verify parallel fan-out, dedup, timeout isolation |
| Complexity routing | Integration | Verify correct sources activated per complexity tier from config |

### 8. Files Changed / Created

**New files (~18):**

| Layer | File | Purpose |
|-------|------|---------|
| Domain | `Domain.AI/RAG/Models/WebSearchResult.cs` | Web search result record |
| Domain | `Domain.AI/RAG/Models/SqlQueryTemplate.cs` | SQL template record |
| Domain | `Domain.AI/RAG/Models/SqlRetrievalResult.cs` | SQL result record |
| Domain | `Domain.Common/Config/AI/RAG/WebSearchConfig.cs` | Web search config |
| Domain | `Domain.Common/Config/AI/RAG/SqlDatabaseConfig.cs` | SQL database config |
| Application | `Application.AI.Common/Interfaces/RAG/IRetrievalSource.cs` | Core abstraction |
| Application | `Application.AI.Common/Interfaces/RAG/IWebSearchProvider.cs` | Web search provider |
| Application | `Application.AI.Common/Interfaces/RAG/ISqlQueryTemplateStore.cs` | Template store |
| Application | `Application.AI.Common/Interfaces/RAG/ISqlQueryExecutor.cs` | SQL executor |
| Infrastructure | `Infrastructure.AI.RAG/Orchestration/VectorRetrievalSource.cs` | Adapter |
| Infrastructure | `Infrastructure.AI.RAG/Orchestration/GraphRetrievalSource.cs` | Adapter |
| Infrastructure | `Infrastructure.AI.RAG/WebSearch/BingWebSearchProvider.cs` | Bing implementation |
| Infrastructure | `Infrastructure.AI.RAG/WebSearch/WebSearchRetrievalSource.cs` | Web adapter |
| Infrastructure | `Infrastructure.AI.RAG/SqlDatabase/JsonSqlQueryTemplateStore.cs` | Template loading |
| Infrastructure | `Infrastructure.AI.RAG/SqlDatabase/SqlQueryTemplateMatcher.cs` | LLM template matching |
| Infrastructure | `Infrastructure.AI.RAG/SqlDatabase/TextToSqlGenerator.cs` | LLM SQL generation |
| Infrastructure | `Infrastructure.AI.RAG/SqlDatabase/SafeSqlQueryExecutor.cs` | Secure SQL execution |
| Infrastructure | `Infrastructure.AI.RAG/SqlDatabase/SqlDatabaseRetrievalSource.cs` | SQL adapter |

**Modified files (~4):**

| File | Change |
|------|--------|
| `Infrastructure.AI.RAG/Orchestration/MultiSourceOrchestrator.cs` | Replace switch with keyed DI resolution |
| `Infrastructure.AI.RAG/Orchestration/RetrievalDecisionGate.cs` | Config-driven complexity→sources mapping |
| `Infrastructure.AI.RAG/DependencyInjection.cs` | Add `AddRagWebSearch()`, `AddRagSqlDatabase()`, refactor `AddRagMultiSource()` |
| `Domain.Common/Config/AI/RAG/MultiSourceConfig.cs` | Add `SourcesByComplexity` mapping |

**Test files (~10):**

One test file per implementation class, plus integration tests for orchestrator fan-out and complexity routing.

### 9. Migration Path

Fully backward-compatible. Existing configurations continue to work:
- `EnabledSources: ["vector", "graph"]` resolves the same two adapters
- New sources (`"web_search"`, `"sql_database"`) are opt-in
- SQL database is disabled by default (`Enabled: false`)
- No breaking changes to `IMultiSourceOrchestrator` public contract
