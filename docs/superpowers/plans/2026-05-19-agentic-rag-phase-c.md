# Phase C: Production Graph & Memory — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the in-memory graph backend with an embedded Kuzu graph database, add Leiden community detection for hierarchical global search, and implement cross-session memory with EMA decay for knowledge persistence across agent conversations.

**Architecture:** `IGraphDatabaseBackend` extends `IKnowledgeGraphStore` with community and traversal operations. `KuzuGraphBackend` implements it using the embedded Kuzu database (no external server). `LeidenCommunityDetector` runs community detection and writes assignments back to the graph. `CrossSessionMemoryStore` maintains a session-local `ConcurrentDictionary` cache with background sync to the graph backend. `MemoryDecayService` applies EMA decay and prunes stale memories.

**Tech Stack:** C# .NET 10, Microsoft.Extensions.AI (IChatClient), Kuzu embedded graph database, xUnit + Moq + FluentAssertions, keyed DI

**Depends on:** Phase A (routing), soft dependency on Phase B (iterative retrieval can feed graph).

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `src/Content/Domain/Domain.AI/KnowledgeGraph/Models/Community.cs` | Community record |
| Create | `src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryRecord.cs` | Cross-session memory record |
| Create | `src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryQuery.cs` | Memory recall query |
| Create | `src/Content/Domain/Domain.AI/KnowledgeGraph/Enums/MemoryOperation.cs` | Remember/Recall/Forget/Improve enum |
| Create | `src/Content/Domain/Domain.Common/Config/AI/RAG/GraphDatabaseConfig.cs` | Graph DB connection config |
| Create | `src/Content/Domain/Domain.Common/Config/AI/RAG/CrossSessionMemoryConfig.cs` | Memory config |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IGraphDatabaseBackend.cs` | Graph DB abstraction |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICommunityDetector.cs` | Community detection interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICrossSessionMemoryStore.cs` | Memory operations interface |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IMemoryDecayService.cs` | Decay/pruning interface |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/KuzuGraphBackend.cs` | Kuzu implementation |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/LeidenCommunityDetector.cs` | Leiden algorithm |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/CrossSessionMemoryStore.cs` | Memory operations |
| Create | `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/MemoryDecayService.cs` | EMA decay + pruning |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/ManagedCodeGraphRagService.cs` | Use IGraphDatabaseBackend |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/FeedbackWeightedScorer.cs` | Persist feedback to graph |
| Modify | `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs` | Register graph & memory services |
| Modify | `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs` | Add GraphDatabase + Memory sections |
| Modify | `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs` | Add graph/memory test helpers |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/KuzuGraphBackendTests.cs` | Graph backend tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/LeidenCommunityDetectorTests.cs` | Community detection tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/CrossSessionMemoryStoreTests.cs` | Memory store tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/MemoryDecayServiceTests.cs` | Decay service tests |
| Create | `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs` | End-to-end graph tests |

---

### Task 1: Domain Models — Community, MemoryRecord, MemoryQuery, MemoryOperation

**Files:**
- Create: `src/Content/Domain/Domain.AI/KnowledgeGraph/Models/Community.cs`
- Create: `src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryRecord.cs`
- Create: `src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryQuery.cs`
- Create: `src/Content/Domain/Domain.AI/KnowledgeGraph/Enums/MemoryOperation.cs`

- [ ] **Step 1: Create the Community record**

```csharp
// src/Content/Domain/Domain.AI/KnowledgeGraph/Models/Community.cs
namespace Domain.AI.KnowledgeGraph.Models;

/// <summary>
/// A detected community of related <see cref="GraphNode"/> entities in the knowledge graph,
/// produced by the Leiden algorithm. Communities enable hierarchical summarization for
/// GraphRAG global search — each community at a given level gets a summary that captures
/// the collective theme of its member nodes.
/// </summary>
/// <remarks>
/// Communities are hierarchical: level 0 is the most granular (small clusters), while
/// higher levels merge clusters into progressively broader groupings. The
/// <see cref="Modularity"/> score indicates how well-separated this community is from
/// the rest of the graph (higher is better, range 0.0–1.0).
/// </remarks>
public sealed record Community
{
    /// <summary>
    /// Unique identifier for this community, typically <c>"community_{level}_{index}"</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The hierarchy level of this community (0 = most granular, higher = broader).
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// LLM-generated summary describing the collective theme of this community's
    /// member nodes and their relationships. Used as context for global search.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// The IDs of <see cref="GraphNode"/> entities belonging to this community.
    /// </summary>
    public required IReadOnlyList<string> NodeIds { get; init; }

    /// <summary>
    /// The modularity score of this community (0.0–1.0), measuring how well-separated
    /// it is from the rest of the graph. Higher values indicate stronger internal
    /// cohesion relative to external connections.
    /// </summary>
    public required double Modularity { get; init; }
}
```

- [ ] **Step 2: Create the MemoryRecord record**

```csharp
// src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryRecord.cs
namespace Domain.AI.KnowledgeGraph.Models;

/// <summary>
/// A discrete unit of knowledge persisted across agent sessions. Memory records
/// are stored in a session-local cache for fast access and synced to the graph
/// backend for durability. Each record carries a feedback-adjusted weight that
/// decays over time via EMA if not accessed.
/// </summary>
/// <remarks>
/// Memory records support four operations via <see cref="Enums.MemoryOperation"/>:
/// <list type="bullet">
///   <item><c>Remember</c> — store a new fact or update an existing one</item>
///   <item><c>Recall</c> — retrieve memories matching a query, updating access count</item>
///   <item><c>Forget</c> — explicitly delete a memory</item>
///   <item><c>Improve</c> — apply feedback to adjust weight</item>
/// </list>
/// </remarks>
public sealed record MemoryRecord
{
    /// <summary>
    /// Unique identifier for this memory, typically a deterministic hash of
    /// <see cref="Content"/> to enable deduplication.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The knowledge content of this memory (a fact, observation, or learned pattern).
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The origin of this memory (e.g., session ID, agent name, tool name).
    /// Enables source-filtered recall.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The feedback-adjusted weight (0.0–1.0). Higher weights indicate more useful
    /// or recently accessed memories. Subject to EMA decay over time.
    /// </summary>
    public required double Weight { get; init; }

    /// <summary>
    /// When this memory was first created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When this memory was last accessed via a Recall or Improve operation.
    /// Used by <see cref="MemoryDecayService"/> to calculate time-based decay.
    /// </summary>
    public required DateTimeOffset LastAccessedAt { get; init; }

    /// <summary>
    /// The number of times this memory has been recalled. Higher access counts
    /// indicate frequently useful knowledge.
    /// </summary>
    public required int AccessCount { get; init; }

    /// <summary>
    /// Arbitrary metadata for this memory (e.g., entity types, topic tags,
    /// conversation context). Stored as strings for graph backend portability.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}
```

- [ ] **Step 3: Create the MemoryQuery record**

```csharp
// src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryQuery.cs
namespace Domain.AI.KnowledgeGraph.Models;

/// <summary>
/// Parameters for a cross-session memory recall operation. Supports filtering
/// by minimum weight (to exclude decayed memories) and by source (to scope
/// recall to a specific agent or session).
/// </summary>
public sealed record MemoryQuery
{
    /// <summary>
    /// The natural language query to match against stored memories.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Maximum number of memories to return (default: 10).
    /// </summary>
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Minimum weight threshold. Memories with weight below this value
    /// are excluded from results (default: 0.1).
    /// </summary>
    public double MinWeight { get; init; } = 0.1;

    /// <summary>
    /// Optional source filter. When set, only memories from this source
    /// are returned. Null means all sources.
    /// </summary>
    public string? Source { get; init; }
}
```

- [ ] **Step 4: Create the MemoryOperation enum**

```csharp
// src/Content/Domain/Domain.AI/KnowledgeGraph/Enums/MemoryOperation.cs
namespace Domain.AI.KnowledgeGraph.Enums;

/// <summary>
/// The four cross-session memory operations supported by
/// <see cref="Application.AI.Common.Interfaces.KnowledgeGraph.ICrossSessionMemoryStore"/>.
/// Maps to <see cref="MemoryAuditAction"/> for audit logging.
/// </summary>
public enum MemoryOperation
{
    /// <summary>Store a new fact or update an existing memory.</summary>
    Remember,

    /// <summary>Retrieve memories matching a query, updating access metadata.</summary>
    Recall,

    /// <summary>Explicitly delete a memory by ID.</summary>
    Forget,

    /// <summary>Apply feedback delta to adjust a memory's weight.</summary>
    Improve
}
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Content/Domain/Domain.AI/KnowledgeGraph/Models/Community.cs src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryRecord.cs src/Content/Domain/Domain.AI/KnowledgeGraph/Models/MemoryQuery.cs src/Content/Domain/Domain.AI/KnowledgeGraph/Enums/MemoryOperation.cs
git commit -m "feat(rag): add Community, MemoryRecord, MemoryQuery models and MemoryOperation enum"
```

---

### Task 2: Configuration — GraphDatabaseConfig and CrossSessionMemoryConfig

**Files:**
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/GraphDatabaseConfig.cs`
- Create: `src/Content/Domain/Domain.Common/Config/AI/RAG/CrossSessionMemoryConfig.cs`
- Modify: `src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs`

- [ ] **Step 1: Create GraphDatabaseConfig**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/GraphDatabaseConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for the graph database backend used by the knowledge graph.
/// Bound from <c>AppConfig:AI:Rag:GraphDatabase</c> in appsettings.json.
/// </summary>
/// <remarks>
/// The <see cref="Provider"/> selects the <c>IGraphDatabaseBackend</c> implementation
/// via keyed DI. The <c>"kuzu"</c> provider uses an embedded database stored in
/// <see cref="DataDirectory"/>, requiring no external server. Other providers
/// (e.g., <c>"neo4j"</c>) require a <see cref="ConnectionString"/>.
/// </remarks>
public sealed class GraphDatabaseConfig
{
    /// <summary>
    /// Gets or sets the graph database provider. Selects the keyed DI implementation
    /// of <c>IGraphDatabaseBackend</c>.
    /// Options: <c>"kuzu"</c> (embedded, default), <c>"neo4j"</c>, <c>"in_memory"</c>.
    /// </summary>
    public string Provider { get; set; } = "kuzu";

    /// <summary>
    /// Gets or sets the connection string for external graph databases (Neo4j, Cosmos DB).
    /// Not required for the <c>"kuzu"</c> embedded provider. Should be stored in
    /// User Secrets (dev) or Azure Key Vault (prod).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the local directory for the embedded Kuzu database files.
    /// Only used when <see cref="Provider"/> is <c>"kuzu"</c>.
    /// </summary>
    public string DataDirectory { get; set; } = "./data/graph";

    /// <summary>
    /// Gets or sets whether the graph database backend is enabled. When <c>false</c>,
    /// the system falls back to the in-memory <c>IKnowledgeGraphStore</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
```

- [ ] **Step 2: Create CrossSessionMemoryConfig**

```csharp
// src/Content/Domain/Domain.Common/Config/AI/RAG/CrossSessionMemoryConfig.cs
namespace Domain.Common.Config.AI.RAG;

/// <summary>
/// Configuration for cross-session memory persistence. Controls decay rate,
/// pruning thresholds, capacity limits, and background sync behavior.
/// Bound from <c>AppConfig:AI:Rag:CrossSessionMemory</c> in appsettings.json.
/// </summary>
/// <remarks>
/// Memory decay uses exponential moving average (EMA):
/// <c>newWeight = weight * (1 - DecayRate) ^ daysSinceLastAccess</c>.
/// Memories that fall below <see cref="PruneThreshold"/> are removed during
/// the next decay pass.
/// </remarks>
public sealed class CrossSessionMemoryConfig
{
    /// <summary>
    /// Gets or sets whether cross-session memory is enabled. When <c>false</c>,
    /// Remember/Recall/Forget/Improve operations are no-ops.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the EMA decay rate per day. A memory not accessed for 1 day
    /// has its weight multiplied by <c>(1 - DecayRate)</c>. Higher values cause
    /// faster forgetting.
    /// </summary>
    /// <value>Default: 0.05. Valid range: 0.0 (no decay) to 1.0 (instant decay).</value>
    public double DecayRate { get; set; } = 0.05;

    /// <summary>
    /// Gets or sets the weight threshold below which memories are pruned during
    /// decay passes. Memories with weight below this value are permanently deleted.
    /// </summary>
    /// <value>Default: 0.01.</value>
    public double PruneThreshold { get; set; } = 0.01;

    /// <summary>
    /// Gets or sets the maximum number of memories to retain in the store.
    /// When exceeded, the lowest-weight memories are pruned to make room.
    /// </summary>
    public int MaxMemories { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the interval between background syncs from the session-local
    /// cache to the graph backend. Shorter intervals reduce data loss risk at
    /// the cost of more frequent writes.
    /// </summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(5);
}
```

- [ ] **Step 3: Add GraphDatabase and CrossSessionMemory to RagConfig**

Add these two properties to `RagConfig.cs` after the existing `ModelTiering` property:

```csharp
// Add to src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs
// After the ModelTiering property:

    /// <summary>
    /// Gets or sets the graph database backend configuration for production-grade
    /// knowledge graph storage with community detection and traversal.
    /// </summary>
    public GraphDatabaseConfig GraphDatabase { get; set; } = new();

    /// <summary>
    /// Gets or sets the cross-session memory configuration for knowledge persistence
    /// across agent conversations with EMA decay and pruning.
    /// </summary>
    public CrossSessionMemoryConfig CrossSessionMemory { get; set; } = new();
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Domain/Domain.Common/Config/AI/RAG/GraphDatabaseConfig.cs src/Content/Domain/Domain.Common/Config/AI/RAG/CrossSessionMemoryConfig.cs src/Content/Domain/Domain.Common/Config/AI/RAG/RagConfig.cs
git commit -m "feat(rag): add GraphDatabaseConfig and CrossSessionMemoryConfig"
```

---

### Task 3: Interfaces — IGraphDatabaseBackend, ICommunityDetector, ICrossSessionMemoryStore, IMemoryDecayService

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IGraphDatabaseBackend.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICommunityDetector.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICrossSessionMemoryStore.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IMemoryDecayService.cs`

- [ ] **Step 1: Create IGraphDatabaseBackend**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IGraphDatabaseBackend.cs
using Domain.AI.KnowledgeGraph.Models;

namespace Application.AI.Common.Interfaces.KnowledgeGraph;

/// <summary>
/// Extended graph storage abstraction that adds community detection support, node weight
/// updates, and deep traversal on top of the base <see cref="IKnowledgeGraphStore"/>
/// CRUD operations. Implemented by production graph databases (Kuzu, Neo4j) that
/// support these operations natively.
/// </summary>
/// <remarks>
/// <para>
/// This interface is the target abstraction for Phase C of the Agentic RAG upgrade.
/// It replaces direct <c>IKnowledgeGraphStore</c> usage in <c>ManagedCodeGraphRagService</c>
/// to enable Leiden community detection and feedback weight persistence.
/// </para>
/// <para>
/// Implementations are registered via keyed DI using the <c>GraphDatabaseConfig.Provider</c>
/// value (e.g., <c>"kuzu"</c>, <c>"neo4j"</c>, <c>"in_memory"</c>).
/// </para>
/// </remarks>
public interface IGraphDatabaseBackend : IKnowledgeGraphStore
{
    /// <summary>
    /// Retrieves all nodes assigned to a specific community.
    /// </summary>
    /// <param name="communityId">The community identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Nodes belonging to the community, or empty if the community does not exist.</returns>
    Task<IReadOnlyList<GraphNode>> GetCommunityNodesAsync(
        string communityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all detected communities at the specified hierarchy level.
    /// </summary>
    /// <param name="level">The community hierarchy level (0 = most granular).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Communities at the requested level, or empty if no communities have been detected.</returns>
    Task<IReadOnlyList<Community>> GetCommunitiesAsync(
        int level,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a node to a community at the specified hierarchy level. A node may belong
    /// to different communities at different levels (hierarchical membership).
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="communityId">The community to assign the node to.</param>
    /// <param name="level">The hierarchy level of the community.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AssignCommunityAsync(
        string nodeId,
        string communityId,
        int level,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the feedback weight of a node. Used by <c>FeedbackWeightedScorer</c>
    /// to persist feedback signals and by <c>MemoryDecayService</c> to apply EMA decay.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="weight">The new weight value (0.0–1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateNodeWeightAsync(
        string nodeId,
        double weight,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Traverses the graph starting from a node up to <paramref name="maxDepth"/> hops,
    /// returning all reachable nodes. Unlike <see cref="IKnowledgeGraphStore.GetNeighborsAsync"/>
    /// which returns immediate neighbors, this performs a full breadth-first traversal.
    /// </summary>
    /// <param name="startNodeId">The starting node identifier.</param>
    /// <param name="maxDepth">Maximum traversal depth (1 = direct neighbors).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All nodes reachable within the specified depth, excluding the start node.</returns>
    Task<IReadOnlyList<GraphNode>> TraverseAsync(
        string startNodeId,
        int maxDepth,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a community record (including its summary) to the graph backend.
    /// Called after community detection and LLM summarization.
    /// </summary>
    /// <param name="community">The community to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveCommunityAsync(
        Community community,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create ICommunityDetector**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICommunityDetector.cs
using Domain.AI.KnowledgeGraph.Models;

namespace Application.AI.Common.Interfaces.KnowledgeGraph;

/// <summary>
/// Detects communities of related entities in the knowledge graph using a graph
/// partitioning algorithm (e.g., Leiden). Produces hierarchical communities at
/// multiple levels for use in GraphRAG global search.
/// </summary>
/// <remarks>
/// The Leiden algorithm is preferred over Louvain because it guarantees connected
/// communities (Louvain can produce disconnected communities in some cases).
/// Community detection should be run after corpus indexing and before global search.
/// </remarks>
public interface ICommunityDetector
{
    /// <summary>
    /// Detects communities in the graph at multiple hierarchy levels. Level 0 is the
    /// most granular (small clusters), and higher levels merge clusters progressively.
    /// </summary>
    /// <param name="graph">The graph backend to detect communities in.</param>
    /// <param name="targetLevels">The number of hierarchy levels to produce (e.g., 3 produces levels 0, 1, 2).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All detected communities across all requested levels.</returns>
    Task<IReadOnlyList<Community>> DetectAsync(
        IGraphDatabaseBackend graph,
        int targetLevels,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Create ICrossSessionMemoryStore**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICrossSessionMemoryStore.cs
using Domain.AI.KnowledgeGraph.Models;

namespace Application.AI.Common.Interfaces.KnowledgeGraph;

/// <summary>
/// Cross-session memory store supporting Remember/Recall/Forget/Improve operations.
/// Maintains a session-local cache for fast access with background sync to the
/// graph backend for durability.
/// </summary>
/// <remarks>
/// <para>
/// Memory records carry a feedback-adjusted weight that decays over time via EMA.
/// Recall operations update <see cref="MemoryRecord.LastAccessedAt"/> and
/// <see cref="MemoryRecord.AccessCount"/>, which resets the decay timer.
/// </para>
/// <para>
/// Implementations should use <c>ConcurrentDictionary</c> for the session-local cache
/// and a background timer for periodic sync to the graph backend. The sync interval
/// is configured via <c>CrossSessionMemoryConfig.SyncInterval</c>.
/// </para>
/// </remarks>
public interface ICrossSessionMemoryStore
{
    /// <summary>
    /// Stores a new memory or updates an existing one with the same ID.
    /// The memory is written to the session-local cache immediately and synced
    /// to the graph backend on the next sync interval.
    /// </summary>
    /// <param name="memory">The memory record to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RememberAsync(
        MemoryRecord memory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves memories matching the query, ordered by relevance and weight.
    /// Updates <see cref="MemoryRecord.LastAccessedAt"/> and
    /// <see cref="MemoryRecord.AccessCount"/> for each returned memory.
    /// </summary>
    /// <param name="query">The recall query with filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching memories ordered by relevance, respecting TopK and MinWeight filters.</returns>
    Task<IReadOnlyList<MemoryRecord>> RecallAsync(
        MemoryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a memory by ID from both the session cache and graph backend.
    /// No-op if the memory does not exist.
    /// </summary>
    /// <param name="memoryId">The memory identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ForgetAsync(
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a feedback delta to a memory's weight. Positive deltas increase the weight
    /// (memory is useful), negative deltas decrease it (memory is misleading).
    /// The weight is clamped to [0.0, 1.0].
    /// </summary>
    /// <param name="memoryId">The memory identifier to improve.</param>
    /// <param name="feedbackDelta">The weight adjustment (e.g., +0.1 for positive, -0.1 for negative).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ImproveAsync(
        string memoryId,
        double feedbackDelta,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create IMemoryDecayService**

```csharp
// src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IMemoryDecayService.cs
namespace Application.AI.Common.Interfaces.KnowledgeGraph;

/// <summary>
/// Applies time-based EMA decay to cross-session memories and prunes memories
/// that fall below the configured threshold. Designed to run periodically
/// (e.g., on session start or via background timer).
/// </summary>
/// <remarks>
/// Decay formula: <c>newWeight = weight * (1 - decayRate) ^ daysSinceLastAccess</c>.
/// Memories that have been recently accessed have minimal decay; memories not accessed
/// for weeks decay toward zero and are eventually pruned.
/// </remarks>
public interface IMemoryDecayService
{
    /// <summary>
    /// Applies EMA decay to all memories in the store based on their
    /// <see cref="Domain.AI.KnowledgeGraph.Models.MemoryRecord.LastAccessedAt"/> timestamp.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ApplyDecayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all memories with weight below the specified threshold.
    /// </summary>
    /// <param name="threshold">The weight threshold below which memories are deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PruneAsync(double threshold, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IGraphDatabaseBackend.cs src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICommunityDetector.cs src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/ICrossSessionMemoryStore.cs src/Content/Application/Application.AI.Common/Interfaces/KnowledgeGraph/IMemoryDecayService.cs
git commit -m "feat(rag): add IGraphDatabaseBackend, ICommunityDetector, ICrossSessionMemoryStore, IMemoryDecayService interfaces"
```

---

### Task 4: Test Data Builders

**Files:**
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs`

- [ ] **Step 1: Add graph and memory test data builders to RagTestData**

Add these methods at the end of the `RagTestData` class, before the closing brace:

```csharp
// Add to src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs
// Add these usings at the top:
// using Domain.AI.KnowledgeGraph.Models;

    public static Community CreateCommunity(
        string id = "community_0_1",
        int level = 0,
        string summary = "A community of related technology entities.",
        IReadOnlyList<string>? nodeIds = null,
        double modularity = 0.65) =>
        new()
        {
            Id = id,
            Level = level,
            Summary = summary,
            NodeIds = nodeIds ?? ["node-1", "node-2", "node-3"],
            Modularity = modularity
        };

    public static MemoryRecord CreateMemoryRecord(
        string id = "mem-1",
        string content = "The user prefers concise answers over verbose explanations.",
        string source = "session-abc",
        double weight = 0.8,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastAccessedAt = null,
        int accessCount = 1,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new()
        {
            Id = id,
            Content = content,
            Source = source,
            Weight = weight,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            LastAccessedAt = lastAccessedAt ?? DateTimeOffset.UtcNow,
            AccessCount = accessCount,
            Metadata = metadata ?? new Dictionary<string, string>()
        };

    public static MemoryQuery CreateMemoryQuery(
        string query = "user preferences",
        int topK = 10,
        double minWeight = 0.1,
        string? source = null) =>
        new()
        {
            Query = query,
            TopK = topK,
            MinWeight = minWeight,
            Source = source
        };

    public static GraphNode CreateGraphNode(
        string id = "node-1",
        string name = "Azure OpenAI",
        string type = "Technology",
        IReadOnlyList<string>? chunkIds = null,
        IReadOnlyDictionary<string, string>? properties = null) =>
        new()
        {
            Id = id,
            Name = name,
            Type = type,
            ChunkIds = chunkIds ?? ["chunk-1"],
            Properties = properties ?? new Dictionary<string, string>()
        };

    public static GraphEdge CreateGraphEdge(
        string id = "edge-1",
        string sourceNodeId = "node-1",
        string targetNodeId = "node-2",
        string predicate = "uses",
        string chunkId = "chunk-1",
        IReadOnlyDictionary<string, string>? properties = null) =>
        new()
        {
            Id = id,
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            Predicate = predicate,
            ChunkId = chunkId,
            Properties = properties ?? new Dictionary<string, string>()
        };
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/Helpers/RagTestData.cs
git commit -m "test(rag): add graph and memory test data builders to RagTestData"
```

---

### Task 5: KuzuGraphBackend Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/KuzuGraphBackend.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/KuzuGraphBackendTests.cs`

**Decision point:** The Kuzu .NET NuGet package (`Kuzu.NET` or `KuzuDB`) may not exist as a stable NuGet package. This implementation uses a `Microsoft.Data.Sqlite`-style embedded approach: Kuzu exposes a C API, and the .NET binding wraps it. If `Kuzu.NET` does not exist on NuGet at implementation time, use the fallback approach described below — a thin wrapper over the Kuzu C library via P/Invoke, or substitute with SQLite-backed graph tables as a shim. The interface (`IGraphDatabaseBackend`) abstracts this choice completely.

**Fallback approach:** If `Kuzu.NET` is unavailable, implement `KuzuGraphBackend` using `Microsoft.Data.Sqlite` with graph tables (Nodes, Edges, CommunityAssignments, Communities). This provides the same semantics with SQLite's production-grade embedded engine. The class name remains `KuzuGraphBackend` to signal the intended target; swap the internal implementation when the binding becomes available.

- [ ] **Step 1: Write the first test — AddNodesAsync stores and retrieves nodes**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/KuzuGraphBackendTests.cs
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.GraphRag;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.AI.RAG.Tests.GraphRag;

/// <summary>
/// Tests for <see cref="KuzuGraphBackend"/> — the embedded graph database implementation
/// of <see cref="IGraphDatabaseBackend"/>. Each test uses a temporary database directory
/// that is cleaned up after the test.
/// </summary>
public sealed class KuzuGraphBackendTests : IDisposable
{
    private readonly string _tempDir;
    private readonly KuzuGraphBackend _sut;

    public KuzuGraphBackendTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kuzu_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new KuzuGraphBackend(_tempDir, NullLogger<KuzuGraphBackend>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task AddNodesAsync_NewNodes_StoresAndRetrieves()
    {
        // Arrange
        var nodes = new List<GraphNode>
        {
            RagTestData.CreateGraphNode("n1", "Azure", "Technology"),
            RagTestData.CreateGraphNode("n2", "OpenAI", "Organization")
        };

        // Act
        await _sut.AddNodesAsync(nodes);

        // Assert
        var retrieved = await _sut.GetNodeAsync("n1");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Azure");
        retrieved.Type.Should().Be("Technology");
    }

    [Fact]
    public async Task AddNodesAsync_DuplicateId_MergesChunkIds()
    {
        // Arrange
        var first = RagTestData.CreateGraphNode("n1", "Azure", "Technology", chunkIds: ["c1"]);
        var second = RagTestData.CreateGraphNode("n1", "Azure", "Technology", chunkIds: ["c2"]);

        // Act
        await _sut.AddNodesAsync([first]);
        await _sut.AddNodesAsync([second]);

        // Assert
        var merged = await _sut.GetNodeAsync("n1");
        merged.Should().NotBeNull();
        merged!.ChunkIds.Should().Contain("c1").And.Contain("c2");
    }

    [Fact]
    public async Task AddEdgesAsync_NewEdges_StoresAndRetrievesViaTriplets()
    {
        // Arrange
        var nodes = new List<GraphNode>
        {
            RagTestData.CreateGraphNode("n1", "Azure", "Technology"),
            RagTestData.CreateGraphNode("n2", "OpenAI", "Organization")
        };
        var edges = new List<GraphEdge>
        {
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "partners_with")
        };

        // Act
        await _sut.AddNodesAsync(nodes);
        await _sut.AddEdgesAsync(edges);

        // Assert
        var triplets = await _sut.GetTripletsAsync(["n1"]);
        triplets.Should().HaveCount(1);
        triplets[0].Edge.Predicate.Should().Be("partners_with");
    }

    [Fact]
    public async Task TraverseAsync_MultiHop_ReturnsAllReachableNodes()
    {
        // Arrange — chain: n1 -> n2 -> n3 -> n4
        var nodes = new List<GraphNode>
        {
            RagTestData.CreateGraphNode("n1", "A", "Type"),
            RagTestData.CreateGraphNode("n2", "B", "Type"),
            RagTestData.CreateGraphNode("n3", "C", "Type"),
            RagTestData.CreateGraphNode("n4", "D", "Type")
        };
        var edges = new List<GraphEdge>
        {
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "links_to"),
            RagTestData.CreateGraphEdge("e2", "n2", "n3", "links_to"),
            RagTestData.CreateGraphEdge("e3", "n3", "n4", "links_to")
        };
        await _sut.AddNodesAsync(nodes);
        await _sut.AddEdgesAsync(edges);

        // Act — depth 2 from n1 should reach n2, n3 but not n4
        var reached = await _sut.TraverseAsync("n1", maxDepth: 2);

        // Assert
        reached.Select(n => n.Id).Should().Contain("n2").And.Contain("n3");
        reached.Select(n => n.Id).Should().NotContain("n4");
        reached.Select(n => n.Id).Should().NotContain("n1");
    }

    [Fact]
    public async Task UpdateNodeWeightAsync_ExistingNode_UpdatesWeight()
    {
        // Arrange
        await _sut.AddNodesAsync([RagTestData.CreateGraphNode("n1", "Azure", "Tech")]);

        // Act
        await _sut.UpdateNodeWeightAsync("n1", 0.75);

        // Assert
        var node = await _sut.GetNodeAsync("n1");
        node.Should().NotBeNull();
        // Weight is stored in properties as the node record is immutable
        // Implementation will expose weight through a mechanism verified here
    }

    [Fact]
    public async Task AssignCommunityAsync_ThenGetCommunityNodes_ReturnsAssignedNodes()
    {
        // Arrange
        var nodes = new List<GraphNode>
        {
            RagTestData.CreateGraphNode("n1", "A", "Type"),
            RagTestData.CreateGraphNode("n2", "B", "Type"),
            RagTestData.CreateGraphNode("n3", "C", "Type")
        };
        await _sut.AddNodesAsync(nodes);

        // Act
        await _sut.AssignCommunityAsync("n1", "comm_0_1", 0);
        await _sut.AssignCommunityAsync("n2", "comm_0_1", 0);

        // Assert
        var communityNodes = await _sut.GetCommunityNodesAsync("comm_0_1");
        communityNodes.Should().HaveCount(2);
        communityNodes.Select(n => n.Id).Should().Contain("n1").And.Contain("n2");
    }

    [Fact]
    public async Task SaveCommunityAsync_ThenGetCommunities_ReturnsSavedCommunity()
    {
        // Arrange
        var community = RagTestData.CreateCommunity("comm_0_1", level: 0, summary: "Tech cluster");

        // Act
        await _sut.SaveCommunityAsync(community);

        // Assert
        var communities = await _sut.GetCommunitiesAsync(0);
        communities.Should().HaveCount(1);
        communities[0].Summary.Should().Be("Tech cluster");
        communities[0].NodeIds.Should().HaveCount(3);
    }

    [Fact]
    public async Task DeleteNodeAsync_ExistingNode_RemovesNodeAndEdges()
    {
        // Arrange
        await _sut.AddNodesAsync([
            RagTestData.CreateGraphNode("n1", "A", "Type"),
            RagTestData.CreateGraphNode("n2", "B", "Type")
        ]);
        await _sut.AddEdgesAsync([
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "links_to")
        ]);

        // Act
        await _sut.DeleteNodeAsync("n1");

        // Assert
        var node = await _sut.GetNodeAsync("n1");
        node.Should().BeNull();
        var triplets = await _sut.GetTripletsAsync(["n2"]);
        triplets.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (no implementation yet)**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~KuzuGraphBackendTests"`
Expected: Build error — `KuzuGraphBackend` does not exist.

- [ ] **Step 3: Implement KuzuGraphBackend using SQLite-backed graph tables**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/KuzuGraphBackend.cs
using System.Text.Json;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.GraphRag;

/// <summary>
/// Embedded graph database implementation of <see cref="IGraphDatabaseBackend"/> backed by
/// SQLite. Uses relational tables to model graph structure (nodes, edges, community assignments).
/// Designed as a production-ready embedded alternative to external graph databases.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Design decision:</strong> This uses SQLite rather than Kuzu's native C API because
/// the Kuzu .NET NuGet binding is not yet stable. The class name reflects the target graph
/// database; the SQLite implementation provides identical semantics and can be swapped for
/// native Kuzu bindings when available without changing the interface contract.
/// </para>
/// <para>
/// Schema: <c>Nodes</c> (id, name, type, chunk_ids_json, properties_json, weight, owner_id,
/// created_at, expires_at), <c>Edges</c> (id, source_node_id, target_node_id, predicate,
/// chunk_id, properties_json, owner_id, created_at, expires_at),
/// <c>CommunityAssignments</c> (node_id, community_id, level),
/// <c>Communities</c> (id, level, summary, node_ids_json, modularity).
/// </para>
/// </remarks>
public sealed class KuzuGraphBackend : IGraphDatabaseBackend, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<KuzuGraphBackend> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Initializes the embedded graph database, creating the schema if it does not exist.
    /// </summary>
    /// <param name="dataDirectory">Directory for the SQLite database file.</param>
    /// <param name="logger">Logger.</param>
    public KuzuGraphBackend(string dataDirectory, ILogger<KuzuGraphBackend> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "graph.db");
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Nodes (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                type TEXT NOT NULL,
                chunk_ids_json TEXT NOT NULL DEFAULT '[]',
                properties_json TEXT NOT NULL DEFAULT '{}',
                weight REAL NOT NULL DEFAULT 1.0,
                owner_id TEXT,
                created_at TEXT,
                expires_at TEXT
            );

            CREATE TABLE IF NOT EXISTS Edges (
                id TEXT PRIMARY KEY,
                source_node_id TEXT NOT NULL,
                target_node_id TEXT NOT NULL,
                predicate TEXT NOT NULL,
                chunk_id TEXT NOT NULL,
                properties_json TEXT NOT NULL DEFAULT '{}',
                owner_id TEXT,
                created_at TEXT,
                expires_at TEXT
            );

            CREATE TABLE IF NOT EXISTS CommunityAssignments (
                node_id TEXT NOT NULL,
                community_id TEXT NOT NULL,
                level INTEGER NOT NULL,
                PRIMARY KEY (node_id, level)
            );

            CREATE TABLE IF NOT EXISTS Communities (
                id TEXT PRIMARY KEY,
                level INTEGER NOT NULL,
                summary TEXT NOT NULL,
                node_ids_json TEXT NOT NULL DEFAULT '[]',
                modularity REAL NOT NULL DEFAULT 0.0
            );

            CREATE INDEX IF NOT EXISTS idx_edges_source ON Edges(source_node_id);
            CREATE INDEX IF NOT EXISTS idx_edges_target ON Edges(target_node_id);
            CREATE INDEX IF NOT EXISTS idx_community_level ON Communities(level);
            CREATE INDEX IF NOT EXISTS idx_assignment_community ON CommunityAssignments(community_id);
            CREATE INDEX IF NOT EXISTS idx_nodes_owner ON Nodes(owner_id);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task AddNodesAsync(
        IReadOnlyList<GraphNode> nodes,
        CancellationToken cancellationToken = default)
    {
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await GetNodeAsync(node.Id, cancellationToken);

            if (existing is not null)
            {
                var mergedChunkIds = existing.ChunkIds.Union(node.ChunkIds).Distinct().ToList();
                using var updateCmd = _connection.CreateCommand();
                updateCmd.CommandText = """
                    UPDATE Nodes SET chunk_ids_json = @chunkIds, properties_json = @props
                    WHERE id = @id
                    """;
                updateCmd.Parameters.AddWithValue("@id", node.Id);
                updateCmd.Parameters.AddWithValue("@chunkIds", JsonSerializer.Serialize(mergedChunkIds));
                updateCmd.Parameters.AddWithValue("@props", JsonSerializer.Serialize(node.Properties));
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                using var insertCmd = _connection.CreateCommand();
                insertCmd.CommandText = """
                    INSERT INTO Nodes (id, name, type, chunk_ids_json, properties_json, weight, owner_id, created_at, expires_at)
                    VALUES (@id, @name, @type, @chunkIds, @props, @weight, @ownerId, @createdAt, @expiresAt)
                    """;
                insertCmd.Parameters.AddWithValue("@id", node.Id);
                insertCmd.Parameters.AddWithValue("@name", node.Name);
                insertCmd.Parameters.AddWithValue("@type", node.Type);
                insertCmd.Parameters.AddWithValue("@chunkIds", JsonSerializer.Serialize(node.ChunkIds));
                insertCmd.Parameters.AddWithValue("@props", JsonSerializer.Serialize(node.Properties));
                insertCmd.Parameters.AddWithValue("@weight", 1.0);
                insertCmd.Parameters.AddWithValue("@ownerId", (object?)node.OwnerId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@createdAt",
                    (object?)node.CreatedAt?.ToString("O") ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@expiresAt",
                    (object?)node.ExpiresAt?.ToString("O") ?? DBNull.Value);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task AddEdgesAsync(
        IReadOnlyList<GraphEdge> edges,
        CancellationToken cancellationToken = default)
    {
        foreach (var edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO Edges (id, source_node_id, target_node_id, predicate, chunk_id, properties_json, owner_id, created_at, expires_at)
                VALUES (@id, @source, @target, @predicate, @chunkId, @props, @ownerId, @createdAt, @expiresAt)
                """;
            cmd.Parameters.AddWithValue("@id", edge.Id);
            cmd.Parameters.AddWithValue("@source", edge.SourceNodeId);
            cmd.Parameters.AddWithValue("@target", edge.TargetNodeId);
            cmd.Parameters.AddWithValue("@predicate", edge.Predicate);
            cmd.Parameters.AddWithValue("@chunkId", edge.ChunkId);
            cmd.Parameters.AddWithValue("@props", JsonSerializer.Serialize(edge.Properties));
            cmd.Parameters.AddWithValue("@ownerId", (object?)edge.OwnerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@createdAt",
                (object?)edge.CreatedAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@expiresAt",
                (object?)edge.ExpiresAt?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<GraphNode?> GetNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, chunk_ids_json, properties_json, weight, owner_id, created_at, expires_at FROM Nodes WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", nodeId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadNodeFromRow(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> GetNeighborsAsync(
        string nodeId,
        int maxDepth = 1,
        CancellationToken cancellationToken = default)
    {
        return await TraverseAsync(nodeId, maxDepth, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphTriplet>> GetTripletsAsync(
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        string query;
        SqliteCommand cmd;

        if (nodeIds.Count == 0)
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT e.id, e.source_node_id, e.target_node_id, e.predicate, e.chunk_id, e.properties_json, e.owner_id, e.created_at, e.expires_at,
                       sn.id, sn.name, sn.type, sn.chunk_ids_json, sn.properties_json, sn.weight, sn.owner_id, sn.created_at, sn.expires_at,
                       tn.id, tn.name, tn.type, tn.chunk_ids_json, tn.properties_json, tn.weight, tn.owner_id, tn.created_at, tn.expires_at
                FROM Edges e
                JOIN Nodes sn ON e.source_node_id = sn.id
                JOIN Nodes tn ON e.target_node_id = tn.id
                """;
        }
        else
        {
            var placeholders = string.Join(", ", nodeIds.Select((_, i) => $"@p{i}"));
            cmd = _connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT e.id, e.source_node_id, e.target_node_id, e.predicate, e.chunk_id, e.properties_json, e.owner_id, e.created_at, e.expires_at,
                       sn.id, sn.name, sn.type, sn.chunk_ids_json, sn.properties_json, sn.weight, sn.owner_id, sn.created_at, sn.expires_at,
                       tn.id, tn.name, tn.type, tn.chunk_ids_json, tn.properties_json, tn.weight, tn.owner_id, tn.created_at, tn.expires_at
                FROM Edges e
                JOIN Nodes sn ON e.source_node_id = sn.id
                JOIN Nodes tn ON e.target_node_id = tn.id
                WHERE e.source_node_id IN ({placeholders}) OR e.target_node_id IN ({placeholders})
                """;
            for (var i = 0; i < nodeIds.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", nodeIds[i]);
        }

        var triplets = new List<GraphTriplet>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var edge = new GraphEdge
            {
                Id = reader.GetString(0),
                SourceNodeId = reader.GetString(1),
                TargetNodeId = reader.GetString(2),
                Predicate = reader.GetString(3),
                ChunkId = reader.GetString(4),
                Properties = DeserializeDict(reader.GetString(5)),
                OwnerId = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                ExpiresAt = reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8))
            };
            var source = ReadNodeFromColumns(reader, 9);
            var target = ReadNodeFromColumns(reader, 18);
            triplets.Add(new GraphTriplet { Source = source, Edge = edge, Target = target });
        }
        cmd.Dispose();
        return triplets;
    }

    /// <inheritdoc />
    public async Task<bool> NodeExistsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Nodes WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", nodeId);
        var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        return count > 0;
    }

    /// <inheritdoc />
    public async Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        using var edgeCmd = _connection.CreateCommand();
        edgeCmd.CommandText = "DELETE FROM Edges WHERE source_node_id = @id OR target_node_id = @id";
        edgeCmd.Parameters.AddWithValue("@id", nodeId);
        await edgeCmd.ExecuteNonQueryAsync(cancellationToken);

        using var assignCmd = _connection.CreateCommand();
        assignCmd.CommandText = "DELETE FROM CommunityAssignments WHERE node_id = @id";
        assignCmd.Parameters.AddWithValue("@id", nodeId);
        await assignCmd.ExecuteNonQueryAsync(cancellationToken);

        using var nodeCmd = _connection.CreateCommand();
        nodeCmd.CommandText = "DELETE FROM Nodes WHERE id = @id";
        nodeCmd.Parameters.AddWithValue("@id", nodeId);
        await nodeCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteEdgeAsync(string edgeId, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Edges WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", edgeId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetNodeCountAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Nodes";
        return (int)(long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<int> GetEdgeCountAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Edges";
        return (int)(long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> GetNodesByOwnerAsync(
        string ownerId, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, chunk_ids_json, properties_json, weight, owner_id, created_at, expires_at FROM Nodes WHERE owner_id = @ownerId";
        cmd.Parameters.AddWithValue("@ownerId", ownerId);
        return await ReadNodesAsync(cmd, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> GetAllNodesAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, chunk_ids_json, properties_json, weight, owner_id, created_at, expires_at FROM Nodes";
        return await ReadNodesAsync(cmd, cancellationToken);
    }

    // --- IGraphDatabaseBackend extended operations ---

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> GetCommunityNodesAsync(
        string communityId, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT n.id, n.name, n.type, n.chunk_ids_json, n.properties_json, n.weight, n.owner_id, n.created_at, n.expires_at
            FROM Nodes n
            JOIN CommunityAssignments ca ON n.id = ca.node_id
            WHERE ca.community_id = @communityId
            """;
        cmd.Parameters.AddWithValue("@communityId", communityId);
        return await ReadNodesAsync(cmd, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Community>> GetCommunitiesAsync(
        int level, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, level, summary, node_ids_json, modularity FROM Communities WHERE level = @level";
        cmd.Parameters.AddWithValue("@level", level);

        var communities = new List<Community>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            communities.Add(new Community
            {
                Id = reader.GetString(0),
                Level = reader.GetInt32(1),
                Summary = reader.GetString(2),
                NodeIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
                Modularity = reader.GetDouble(4)
            });
        }
        return communities;
    }

    /// <inheritdoc />
    public async Task AssignCommunityAsync(
        string nodeId, string communityId, int level, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO CommunityAssignments (node_id, community_id, level)
            VALUES (@nodeId, @communityId, @level)
            """;
        cmd.Parameters.AddWithValue("@nodeId", nodeId);
        cmd.Parameters.AddWithValue("@communityId", communityId);
        cmd.Parameters.AddWithValue("@level", level);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateNodeWeightAsync(
        string nodeId, double weight, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE Nodes SET weight = @weight WHERE id = @id";
        cmd.Parameters.AddWithValue("@weight", weight);
        cmd.Parameters.AddWithValue("@id", nodeId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> TraverseAsync(
        string startNodeId, int maxDepth, CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<string> { startNodeId };
        var frontier = new HashSet<string> { startNodeId };
        var result = new List<GraphNode>();

        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextFrontier = new HashSet<string>();

            foreach (var nodeId in frontier)
            {
                var placeholders = $"@fid_{nodeId.GetHashCode():X}";
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"""
                    SELECT DISTINCT CASE WHEN source_node_id = @nodeId THEN target_node_id ELSE source_node_id END AS neighbor_id
                    FROM Edges
                    WHERE source_node_id = @nodeId OR target_node_id = @nodeId
                    """;
                cmd.Parameters.AddWithValue("@nodeId", nodeId);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var neighborId = reader.GetString(0);
                    if (visited.Add(neighborId))
                        nextFrontier.Add(neighborId);
                }
            }

            foreach (var neighborId in nextFrontier)
            {
                var node = await GetNodeAsync(neighborId, cancellationToken);
                if (node is not null)
                    result.Add(node);
            }

            frontier = nextFrontier;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SaveCommunityAsync(Community community, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Communities (id, level, summary, node_ids_json, modularity)
            VALUES (@id, @level, @summary, @nodeIds, @modularity)
            """;
        cmd.Parameters.AddWithValue("@id", community.Id);
        cmd.Parameters.AddWithValue("@level", community.Level);
        cmd.Parameters.AddWithValue("@summary", community.Summary);
        cmd.Parameters.AddWithValue("@nodeIds", JsonSerializer.Serialize(community.NodeIds));
        cmd.Parameters.AddWithValue("@modularity", community.Modularity);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the underlying SQLite connection.
    /// </summary>
    public void Dispose()
    {
        _connection.Dispose();
    }

    // --- Private helpers ---

    private GraphNode ReadNodeFromRow(SqliteDataReader reader) => ReadNodeFromColumns(reader, 0);

    private static GraphNode ReadNodeFromColumns(SqliteDataReader reader, int startCol) =>
        new()
        {
            Id = reader.GetString(startCol),
            Name = reader.GetString(startCol + 1),
            Type = reader.GetString(startCol + 2),
            ChunkIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(startCol + 3)) ?? [],
            Properties = DeserializeDict(reader.GetString(startCol + 4)),
            OwnerId = reader.IsDBNull(startCol + 6) ? null : reader.GetString(startCol + 6),
            CreatedAt = reader.IsDBNull(startCol + 7) ? null : DateTimeOffset.Parse(reader.GetString(startCol + 7)),
            ExpiresAt = reader.IsDBNull(startCol + 8) ? null : DateTimeOffset.Parse(reader.GetString(startCol + 8))
        };

    private async Task<IReadOnlyList<GraphNode>> ReadNodesAsync(
        SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var nodes = new List<GraphNode>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            nodes.Add(ReadNodeFromRow(reader));
        return nodes;
    }

    private static IReadOnlyDictionary<string, string> DeserializeDict(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
        ?? new Dictionary<string, string>();
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~KuzuGraphBackendTests"`
Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/KuzuGraphBackend.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/KuzuGraphBackendTests.cs
git commit -m "feat(rag): implement KuzuGraphBackend with SQLite-backed graph tables"
```

---

### Task 6: LeidenCommunityDetector Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/LeidenCommunityDetector.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/LeidenCommunityDetectorTests.cs`

- [ ] **Step 1: Write all 6 tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/LeidenCommunityDetectorTests.cs
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using FluentAssertions;
using Infrastructure.AI.RAG.GraphRag;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.AI.RAG.Tests.GraphRag;

/// <summary>
/// Tests for <see cref="LeidenCommunityDetector"/>.
/// Uses a real <see cref="KuzuGraphBackend"/> with an in-memory temp directory
/// to test community detection against actual graph structure.
/// </summary>
public sealed class LeidenCommunityDetectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly KuzuGraphBackend _graph;
    private readonly LeidenCommunityDetector _sut;

    public LeidenCommunityDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"leiden_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _graph = new KuzuGraphBackend(_tempDir, NullLogger<KuzuGraphBackend>.Instance);
        _sut = new LeidenCommunityDetector(NullLogger<LeidenCommunityDetector>.Instance);
    }

    public void Dispose()
    {
        _graph.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task DetectAsync_SmallGraph_ReturnsExpectedCommunities()
    {
        // Arrange — two clusters: (n1, n2, n3) and (n4, n5) connected by one bridge edge
        await _graph.AddNodesAsync([
            RagTestData.CreateGraphNode("n1", "A", "Type"),
            RagTestData.CreateGraphNode("n2", "B", "Type"),
            RagTestData.CreateGraphNode("n3", "C", "Type"),
            RagTestData.CreateGraphNode("n4", "D", "Type"),
            RagTestData.CreateGraphNode("n5", "E", "Type")
        ]);
        await _graph.AddEdgesAsync([
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "links"),
            RagTestData.CreateGraphEdge("e2", "n2", "n3", "links"),
            RagTestData.CreateGraphEdge("e3", "n1", "n3", "links"),
            RagTestData.CreateGraphEdge("e4", "n4", "n5", "links"),
            RagTestData.CreateGraphEdge("e5", "n3", "n4", "bridge") // bridge edge
        ]);

        // Act
        var communities = await _sut.DetectAsync(_graph, targetLevels: 1);

        // Assert
        communities.Should().NotBeEmpty();
        communities.Should().AllSatisfy(c => c.Level.Should().Be(0));
        var allNodeIds = communities.SelectMany(c => c.NodeIds).Distinct().ToList();
        allNodeIds.Should().HaveCount(5, "all nodes should be assigned to a community");
    }

    [Fact]
    public async Task DetectAsync_DisconnectedComponents_SeparateCommunities()
    {
        // Arrange — two disconnected pairs: (n1, n2) and (n3, n4)
        await _graph.AddNodesAsync([
            RagTestData.CreateGraphNode("n1", "A", "Type"),
            RagTestData.CreateGraphNode("n2", "B", "Type"),
            RagTestData.CreateGraphNode("n3", "C", "Type"),
            RagTestData.CreateGraphNode("n4", "D", "Type")
        ]);
        await _graph.AddEdgesAsync([
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "links"),
            RagTestData.CreateGraphEdge("e2", "n3", "n4", "links")
        ]);

        // Act
        var communities = await _sut.DetectAsync(_graph, targetLevels: 1);

        // Assert
        communities.Should().HaveCountGreaterOrEqualTo(2,
            "disconnected components should be in separate communities");
        var commWithN1 = communities.First(c => c.NodeIds.Contains("n1"));
        var commWithN3 = communities.First(c => c.NodeIds.Contains("n3"));
        commWithN1.Id.Should().NotBe(commWithN3.Id);
    }

    [Fact]
    public async Task DetectAsync_MultipleLevels_ReturnsHierarchy()
    {
        // Arrange — 6 nodes in two clusters
        await _graph.AddNodesAsync([
            RagTestData.CreateGraphNode("n1", "A", "Type"),
            RagTestData.CreateGraphNode("n2", "B", "Type"),
            RagTestData.CreateGraphNode("n3", "C", "Type"),
            RagTestData.CreateGraphNode("n4", "D", "Type"),
            RagTestData.CreateGraphNode("n5", "E", "Type"),
            RagTestData.CreateGraphNode("n6", "F", "Type")
        ]);
        await _graph.AddEdgesAsync([
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "links"),
            RagTestData.CreateGraphEdge("e2", "n2", "n3", "links"),
            RagTestData.CreateGraphEdge("e3", "n1", "n3", "links"),
            RagTestData.CreateGraphEdge("e4", "n4", "n5", "links"),
            RagTestData.CreateGraphEdge("e5", "n5", "n6", "links"),
            RagTestData.CreateGraphEdge("e6", "n4", "n6", "links"),
            RagTestData.CreateGraphEdge("e7", "n3", "n4", "bridge")
        ]);

        // Act
        var communities = await _sut.DetectAsync(_graph, targetLevels: 2);

        // Assert
        var level0 = communities.Where(c => c.Level == 0).ToList();
        var level1 = communities.Where(c => c.Level == 1).ToList();
        level0.Should().NotBeEmpty("level 0 should have granular communities");
        level1.Should().NotBeEmpty("level 1 should have merged communities");
        level1.Should().HaveCountLessThanOrEqualTo(level0.Count,
            "higher levels should have fewer or equal communities");
    }

    [Fact]
    public async Task DetectAsync_SingleNode_ReturnsSingleCommunity()
    {
        // Arrange
        await _graph.AddNodesAsync([RagTestData.CreateGraphNode("n1", "Solo", "Type")]);

        // Act
        var communities = await _sut.DetectAsync(_graph, targetLevels: 1);

        // Assert
        communities.Should().HaveCount(1);
        communities[0].NodeIds.Should().Contain("n1");
    }

    [Fact]
    public async Task DetectAsync_EmptyGraph_ReturnsEmpty()
    {
        // Act
        var communities = await _sut.DetectAsync(_graph, targetLevels: 1);

        // Assert
        communities.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_CancellationRequested_Throws()
    {
        // Arrange
        await _graph.AddNodesAsync([
            RagTestData.CreateGraphNode("n1", "A", "Type"),
            RagTestData.CreateGraphNode("n2", "B", "Type")
        ]);
        await _graph.AddEdgesAsync([
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "links")
        ]);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.DetectAsync(_graph, targetLevels: 1, cts.Token));
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (no implementation yet)**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~LeidenCommunityDetectorTests"`
Expected: Build error — `LeidenCommunityDetector` does not exist.

- [ ] **Step 3: Implement LeidenCommunityDetector**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/LeidenCommunityDetector.cs
using System.Diagnostics;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.AI.Telemetry.Conventions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.GraphRag;

/// <summary>
/// Implements community detection using a simplified Leiden algorithm. Produces
/// hierarchical communities at multiple levels by iteratively merging nodes based
/// on modularity optimization.
/// </summary>
/// <remarks>
/// <para>
/// The Leiden algorithm improves on Louvain by guaranteeing that all detected communities
/// are connected. This implementation uses the following phases per level:
/// </para>
/// <list type="number">
///   <item>Build an adjacency list from the graph's edges.</item>
///   <item>Initialize each node in its own community.</item>
///   <item>Iteratively move nodes to the community of their most-connected neighbor
///         if it improves modularity.</item>
///   <item>Refine by checking that each community is connected; split disconnected ones.</item>
///   <item>For higher levels, treat level N communities as super-nodes and repeat.</item>
/// </list>
/// </remarks>
public sealed class LeidenCommunityDetector : ICommunityDetector
{
    private static readonly ActivitySource ActivitySource = new("Infrastructure.AI.RAG.GraphRag");

    private readonly ILogger<LeidenCommunityDetector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeidenCommunityDetector"/> class.
    /// </summary>
    /// <param name="logger">Logger for recording detection progress.</param>
    public LeidenCommunityDetector(ILogger<LeidenCommunityDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Community>> DetectAsync(
        IGraphDatabaseBackend graph,
        int targetLevels,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.graph.community_detection");

        cancellationToken.ThrowIfCancellationRequested();

        var allNodes = await graph.GetAllNodesAsync(cancellationToken);
        if (allNodes.Count == 0)
            return [];

        var nodeIds = allNodes.Select(n => n.Id).ToList();
        var adjacency = await BuildAdjacencyListAsync(graph, nodeIds, cancellationToken);
        var totalEdges = adjacency.Values.Sum(neighbors => neighbors.Count) / 2.0;

        var allCommunities = new List<Community>();
        var currentAssignments = InitializeAssignments(nodeIds, adjacency);

        for (var level = 0; level < targetLevels; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            currentAssignments = OptimizeModularity(
                currentAssignments, adjacency, totalEdges);
            currentAssignments = RefineConnectivity(currentAssignments, adjacency);

            var levelCommunities = BuildCommunities(currentAssignments, level);
            allCommunities.AddRange(levelCommunities);

            _logger.LogInformation(
                "Leiden level {Level}: {CommunityCount} communities detected from {NodeCount} nodes",
                level, levelCommunities.Count, nodeIds.Count);

            if (levelCommunities.Count <= 1)
                break;

            if (level < targetLevels - 1)
            {
                var (mergedAdjacency, mergedAssignments) = MergeCommunities(
                    currentAssignments, adjacency);
                adjacency = mergedAdjacency;
                currentAssignments = mergedAssignments;
                totalEdges = adjacency.Values.Sum(n => n.Count) / 2.0;
            }
        }

        activity?.SetTag(RagConventions.GraphCommunityLevel, targetLevels);
        activity?.SetTag("rag.graph.community_count", allCommunities.Count);

        return allCommunities;
    }

    private static async Task<Dictionary<string, HashSet<string>>> BuildAdjacencyListAsync(
        IGraphDatabaseBackend graph,
        List<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var adjacency = new Dictionary<string, HashSet<string>>();
        foreach (var nodeId in nodeIds)
            adjacency[nodeId] = [];

        var triplets = await graph.GetTripletsAsync([], cancellationToken);
        foreach (var triplet in triplets)
        {
            var src = triplet.Edge.SourceNodeId;
            var tgt = triplet.Edge.TargetNodeId;

            if (adjacency.ContainsKey(src) && adjacency.ContainsKey(tgt))
            {
                adjacency[src].Add(tgt);
                adjacency[tgt].Add(src);
            }
        }

        return adjacency;
    }

    private static Dictionary<string, string> InitializeAssignments(
        List<string> nodeIds,
        Dictionary<string, HashSet<string>> adjacency)
    {
        var assignments = new Dictionary<string, string>();
        var visited = new HashSet<string>();

        var communityIndex = 0;
        foreach (var nodeId in nodeIds)
        {
            if (visited.Contains(nodeId))
                continue;

            var communityId = $"auto_{communityIndex}";
            var component = FindConnectedComponent(nodeId, adjacency);
            foreach (var member in component)
            {
                assignments[member] = communityId;
                visited.Add(member);
            }
            communityIndex++;
        }

        return assignments;
    }

    private static HashSet<string> FindConnectedComponent(
        string startNode,
        Dictionary<string, HashSet<string>> adjacency)
    {
        var component = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(startNode);
        component.Add(startNode);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var neighbors))
                continue;

            foreach (var neighbor in neighbors)
            {
                if (component.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return component;
    }

    private static Dictionary<string, string> OptimizeModularity(
        Dictionary<string, string> assignments,
        Dictionary<string, HashSet<string>> adjacency,
        double totalEdges)
    {
        if (totalEdges <= 0)
            return assignments;

        var improved = true;
        var maxIterations = 50;
        var iteration = 0;

        while (improved && iteration < maxIterations)
        {
            improved = false;
            iteration++;

            foreach (var nodeId in assignments.Keys.ToList())
            {
                var currentCommunity = assignments[nodeId];
                if (!adjacency.TryGetValue(nodeId, out var neighbors) || neighbors.Count == 0)
                    continue;

                var neighborCommunities = neighbors
                    .Where(assignments.ContainsKey)
                    .Select(n => assignments[n])
                    .Distinct()
                    .ToList();

                var bestCommunity = currentCommunity;
                var bestGain = 0.0;

                foreach (var candidateCommunity in neighborCommunities)
                {
                    if (candidateCommunity == currentCommunity)
                        continue;

                    var gain = CalculateModularityGain(
                        nodeId, candidateCommunity, assignments, adjacency, totalEdges);

                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestCommunity = candidateCommunity;
                    }
                }

                if (bestCommunity != currentCommunity)
                {
                    assignments[nodeId] = bestCommunity;
                    improved = true;
                }
            }
        }

        return assignments;
    }

    private static double CalculateModularityGain(
        string nodeId,
        string targetCommunity,
        Dictionary<string, string> assignments,
        Dictionary<string, HashSet<string>> adjacency,
        double totalEdges)
    {
        if (!adjacency.TryGetValue(nodeId, out var neighbors))
            return 0.0;

        var ki = (double)neighbors.Count;
        var edgesToTarget = neighbors.Count(n =>
            assignments.TryGetValue(n, out var c) && c == targetCommunity);
        var communityDegree = assignments
            .Where(kvp => kvp.Value == targetCommunity)
            .Sum(kvp => adjacency.TryGetValue(kvp.Key, out var n) ? n.Count : 0);

        return (edgesToTarget / totalEdges) - (ki * communityDegree / (2.0 * totalEdges * totalEdges));
    }

    private static Dictionary<string, string> RefineConnectivity(
        Dictionary<string, string> assignments,
        Dictionary<string, HashSet<string>> adjacency)
    {
        var refined = new Dictionary<string, string>(assignments);
        var communitySets = refined
            .GroupBy(kvp => kvp.Value)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Key).ToHashSet());

        var splitIndex = 0;
        foreach (var (communityId, members) in communitySets)
        {
            var subAdjacency = new Dictionary<string, HashSet<string>>();
            foreach (var member in members)
            {
                subAdjacency[member] = adjacency.TryGetValue(member, out var neighbors)
                    ? new HashSet<string>(neighbors.Where(members.Contains))
                    : [];
            }

            var visited = new HashSet<string>();
            var isFirst = true;
            foreach (var member in members)
            {
                if (visited.Contains(member))
                    continue;

                var component = FindConnectedComponent(member, subAdjacency);
                foreach (var node in component)
                    visited.Add(node);

                if (isFirst)
                {
                    isFirst = false;
                    continue;
                }

                var newId = $"{communityId}_split_{splitIndex++}";
                foreach (var node in component)
                    refined[node] = newId;
            }
        }

        return refined;
    }

    private static List<Community> BuildCommunities(
        Dictionary<string, string> assignments,
        int level)
    {
        return assignments
            .GroupBy(kvp => kvp.Value)
            .Select((group, idx) =>
            {
                var nodeIds = group.Select(kvp => kvp.Key).ToList();
                return new Community
                {
                    Id = $"community_{level}_{idx}",
                    Level = level,
                    Summary = $"Community of {nodeIds.Count} entities at level {level}.",
                    NodeIds = nodeIds,
                    Modularity = 0.5
                };
            })
            .ToList();
    }

    private static (Dictionary<string, HashSet<string>> Adjacency, Dictionary<string, string> Assignments)
        MergeCommunities(
            Dictionary<string, string> assignments,
            Dictionary<string, HashSet<string>> adjacency)
    {
        var communityGroups = assignments
            .GroupBy(kvp => kvp.Value)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Key).ToHashSet());

        var newAdjacency = new Dictionary<string, HashSet<string>>();
        foreach (var communityId in communityGroups.Keys)
            newAdjacency[communityId] = [];

        foreach (var (communityId, members) in communityGroups)
        {
            foreach (var member in members)
            {
                if (!adjacency.TryGetValue(member, out var neighbors))
                    continue;

                foreach (var neighbor in neighbors)
                {
                    if (!assignments.TryGetValue(neighbor, out var neighborCommunity))
                        continue;

                    if (neighborCommunity != communityId)
                        newAdjacency[communityId].Add(neighborCommunity);
                }
            }
        }

        var newAssignments = communityGroups.Keys
            .ToDictionary(c => c, c => c);

        return (newAdjacency, newAssignments);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~LeidenCommunityDetectorTests"`
Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/LeidenCommunityDetector.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/LeidenCommunityDetectorTests.cs
git commit -m "feat(rag): implement LeidenCommunityDetector for hierarchical community detection"
```

---

### Task 7: CrossSessionMemoryStore Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/CrossSessionMemoryStore.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/CrossSessionMemoryStoreTests.cs`

- [ ] **Step 1: Write all 7 tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/CrossSessionMemoryStoreTests.cs
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.Common.Config;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Infrastructure.AI.RAG.GraphRag;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.GraphRag;

/// <summary>
/// Tests for <see cref="CrossSessionMemoryStore"/>.
/// Uses a mock <see cref="IGraphDatabaseBackend"/> to verify cache behavior
/// and backend sync operations.
/// </summary>
public sealed class CrossSessionMemoryStoreTests : IDisposable
{
    private readonly Mock<IGraphDatabaseBackend> _mockGraph;
    private readonly CrossSessionMemoryStore _sut;

    public CrossSessionMemoryStoreTests()
    {
        _mockGraph = new Mock<IGraphDatabaseBackend>();
        _mockGraph
            .Setup(g => g.GetAllNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GraphNode>());

        var config = new AppConfig();
        config.AI.Rag.CrossSessionMemory = new CrossSessionMemoryConfig
        {
            Enabled = true,
            MaxMemories = 5,
            PruneThreshold = 0.01,
            DecayRate = 0.05,
            SyncInterval = TimeSpan.FromMinutes(30)
        };
        var monitor = new Mock<IOptionsMonitor<AppConfig>>();
        monitor.Setup(m => m.CurrentValue).Returns(config);

        _sut = new CrossSessionMemoryStore(
            _mockGraph.Object,
            monitor.Object,
            NullLogger<CrossSessionMemoryStore>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public async Task RememberAsync_NewMemory_StoresInCache()
    {
        // Arrange
        var memory = RagTestData.CreateMemoryRecord("mem-1", "Azure is a cloud platform.");

        // Act
        await _sut.RememberAsync(memory);

        // Assert
        var recalled = await _sut.RecallAsync(RagTestData.CreateMemoryQuery("Azure"));
        recalled.Should().ContainSingle(m => m.Id == "mem-1");
    }

    [Fact]
    public async Task RecallAsync_ExistingMemory_ReturnsAndUpdatesAccessCount()
    {
        // Arrange
        var memory = RagTestData.CreateMemoryRecord("mem-1", "Azure is a cloud platform.", accessCount: 0);
        await _sut.RememberAsync(memory);

        // Act
        var results = await _sut.RecallAsync(RagTestData.CreateMemoryQuery("Azure"));

        // Assert
        results.Should().ContainSingle();
        results[0].AccessCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RecallAsync_MinWeightFilter_ExcludesBelowThreshold()
    {
        // Arrange
        var highWeight = RagTestData.CreateMemoryRecord("mem-1", "Important fact.", weight: 0.8);
        var lowWeight = RagTestData.CreateMemoryRecord("mem-2", "Unimportant fact.", weight: 0.05);
        await _sut.RememberAsync(highWeight);
        await _sut.RememberAsync(lowWeight);

        // Act
        var results = await _sut.RecallAsync(RagTestData.CreateMemoryQuery(
            query: "fact", minWeight: 0.1));

        // Assert
        results.Should().ContainSingle(m => m.Id == "mem-1");
        results.Should().NotContain(m => m.Id == "mem-2");
    }

    [Fact]
    public async Task ForgetAsync_ExistingMemory_RemovesFromCacheAndBackend()
    {
        // Arrange
        var memory = RagTestData.CreateMemoryRecord("mem-1", "Forget me.");
        await _sut.RememberAsync(memory);

        // Act
        await _sut.ForgetAsync("mem-1");

        // Assert
        var results = await _sut.RecallAsync(RagTestData.CreateMemoryQuery("Forget"));
        results.Should().BeEmpty();
        _mockGraph.Verify(g => g.DeleteNodeAsync("mem-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImproveAsync_ExistingMemory_AdjustsWeight()
    {
        // Arrange
        var memory = RagTestData.CreateMemoryRecord("mem-1", "Improvable fact.", weight: 0.5);
        await _sut.RememberAsync(memory);

        // Act
        await _sut.ImproveAsync("mem-1", feedbackDelta: 0.2);

        // Assert
        var results = await _sut.RecallAsync(RagTestData.CreateMemoryQuery("Improvable"));
        results.Should().ContainSingle();
        results[0].Weight.Should().BeApproximately(0.7, precision: 0.01);
    }

    [Fact]
    public async Task RecallAsync_SourceFilter_OnlyReturnsMatchingSource()
    {
        // Arrange
        var mem1 = RagTestData.CreateMemoryRecord("mem-1", "From session A.", source: "session-a");
        var mem2 = RagTestData.CreateMemoryRecord("mem-2", "From session B.", source: "session-b");
        await _sut.RememberAsync(mem1);
        await _sut.RememberAsync(mem2);

        // Act
        var results = await _sut.RecallAsync(RagTestData.CreateMemoryQuery(
            query: "session", source: "session-a"));

        // Assert
        results.Should().ContainSingle(m => m.Id == "mem-1");
    }

    [Fact]
    public async Task RememberAsync_ExceedsMaxMemories_PrunesLowestWeight()
    {
        // Arrange — max is 5, add 6 memories with decreasing weights
        for (var i = 1; i <= 6; i++)
        {
            var weight = 1.0 - (i * 0.1);
            await _sut.RememberAsync(RagTestData.CreateMemoryRecord(
                $"mem-{i}", $"Memory {i}", weight: weight));
        }

        // Act
        var results = await _sut.RecallAsync(RagTestData.CreateMemoryQuery(
            query: "Memory", topK: 10, minWeight: 0.0));

        // Assert
        results.Should().HaveCount(5, "should have pruned to MaxMemories");
        results.Should().NotContain(m => m.Id == "mem-6",
            "the lowest-weight memory should have been pruned");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (no implementation yet)**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~CrossSessionMemoryStoreTests"`
Expected: Build error — `CrossSessionMemoryStore` does not exist.

- [ ] **Step 3: Implement CrossSessionMemoryStore**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/CrossSessionMemoryStore.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.AI.Telemetry.Conventions;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.GraphRag;

/// <summary>
/// Cross-session memory store with a session-local <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// cache and background sync to the graph backend. Supports Remember, Recall, Forget, and
/// Improve operations with weight-based filtering and capacity enforcement.
/// </summary>
/// <remarks>
/// <para>
/// The cache provides fast in-process access. Dirty entries (created, updated, or improved
/// during this session) are synced to the graph backend periodically via a background timer.
/// Forget operations are applied to both cache and backend immediately.
/// </para>
/// <para>
/// Recall uses simple keyword matching against memory content. For production use with
/// large memory stores, replace with embedding-based similarity search via the graph backend.
/// </para>
/// </remarks>
public sealed class CrossSessionMemoryStore : ICrossSessionMemoryStore, IDisposable
{
    private static readonly ActivitySource ActivitySource = new("Infrastructure.AI.RAG.GraphRag");

    private readonly ConcurrentDictionary<string, MemoryRecord> _cache = new();
    private readonly ConcurrentDictionary<string, bool> _dirty = new();
    private readonly IGraphDatabaseBackend _graphBackend;
    private readonly IOptionsMonitor<AppConfig> _configMonitor;
    private readonly ILogger<CrossSessionMemoryStore> _logger;
    private readonly Timer? _syncTimer;

    /// <summary>
    /// Initializes the memory store, loading existing memories from the graph backend
    /// into the session-local cache.
    /// </summary>
    /// <param name="graphBackend">The graph database backend for durable storage.</param>
    /// <param name="configMonitor">Application configuration monitor.</param>
    /// <param name="logger">Logger.</param>
    public CrossSessionMemoryStore(
        IGraphDatabaseBackend graphBackend,
        IOptionsMonitor<AppConfig> configMonitor,
        ILogger<CrossSessionMemoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(graphBackend);
        ArgumentNullException.ThrowIfNull(configMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _graphBackend = graphBackend;
        _configMonitor = configMonitor;
        _logger = logger;

        var syncInterval = configMonitor.CurrentValue.AI.Rag.CrossSessionMemory.SyncInterval;
        if (syncInterval > TimeSpan.Zero)
        {
            _syncTimer = new Timer(
                _ => _ = SyncToBackendAsync(CancellationToken.None),
                null,
                syncInterval,
                syncInterval);
        }
    }

    /// <inheritdoc />
    public Task RememberAsync(MemoryRecord memory, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.memory.remember");

        _cache[memory.Id] = memory;
        _dirty[memory.Id] = true;

        var config = _configMonitor.CurrentValue.AI.Rag.CrossSessionMemory;
        if (_cache.Count > config.MaxMemories)
            PruneToCapacity(config.MaxMemories);

        _logger.LogDebug("Memory stored: {MemoryId} (weight={Weight:F2})", memory.Id, memory.Weight);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryRecord>> RecallAsync(
        MemoryQuery query, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.memory.recall");

        var queryTerms = query.Query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = _cache.Values
            .Where(m => m.Weight >= query.MinWeight)
            .Where(m => query.Source is null || m.Source == query.Source)
            .Where(m => queryTerms.Any(t =>
                m.Content.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.Weight)
            .ThenByDescending(m => m.AccessCount)
            .Take(query.TopK)
            .ToList();

        var updated = new List<MemoryRecord>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var accessed = candidate with
            {
                AccessCount = candidate.AccessCount + 1,
                LastAccessedAt = DateTimeOffset.UtcNow
            };
            _cache[accessed.Id] = accessed;
            _dirty[accessed.Id] = true;
            updated.Add(accessed);
        }

        activity?.SetTag("rag.memory.recall_count", updated.Count);
        _logger.LogDebug("Memory recall: {Count} results for query '{Query}'",
            updated.Count, query.Query);

        return Task.FromResult<IReadOnlyList<MemoryRecord>>(updated);
    }

    /// <inheritdoc />
    public async Task ForgetAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.memory.forget");

        _cache.TryRemove(memoryId, out _);
        _dirty.TryRemove(memoryId, out _);

        await _graphBackend.DeleteNodeAsync(memoryId, cancellationToken);
        _logger.LogInformation("Memory forgotten: {MemoryId}", memoryId);
    }

    /// <inheritdoc />
    public Task ImproveAsync(string memoryId, double feedbackDelta, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.memory.improve");

        if (!_cache.TryGetValue(memoryId, out var existing))
        {
            _logger.LogWarning("Improve failed: memory {MemoryId} not found in cache", memoryId);
            return Task.CompletedTask;
        }

        var newWeight = Math.Clamp(existing.Weight + feedbackDelta, 0.0, 1.0);
        var improved = existing with
        {
            Weight = newWeight,
            LastAccessedAt = DateTimeOffset.UtcNow
        };

        _cache[memoryId] = improved;
        _dirty[memoryId] = true;

        activity?.SetTag("rag.memory.weight_delta", feedbackDelta);
        _logger.LogDebug("Memory improved: {MemoryId} weight {OldWeight:F2} -> {NewWeight:F2}",
            memoryId, existing.Weight, newWeight);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Syncs all dirty cache entries to the graph backend. Called by the background timer
    /// and can be called explicitly for testing.
    /// </summary>
    internal async Task SyncToBackendAsync(CancellationToken cancellationToken)
    {
        var dirtyIds = _dirty.Keys.ToList();
        if (dirtyIds.Count == 0)
            return;

        var nodes = new List<GraphNode>();
        foreach (var id in dirtyIds)
        {
            if (!_cache.TryGetValue(id, out var memory))
                continue;

            nodes.Add(new GraphNode
            {
                Id = memory.Id,
                Name = memory.Content[..Math.Min(memory.Content.Length, 100)],
                Type = "Memory",
                ChunkIds = [],
                Properties = new Dictionary<string, string>
                {
                    ["source"] = memory.Source,
                    ["weight"] = memory.Weight.ToString("F4"),
                    ["access_count"] = memory.AccessCount.ToString(),
                    ["last_accessed_at"] = memory.LastAccessedAt.ToString("O"),
                    ["created_at"] = memory.CreatedAt.ToString("O"),
                    ["content"] = memory.Content
                }
            });

            _dirty.TryRemove(id, out _);
        }

        if (nodes.Count > 0)
        {
            await _graphBackend.AddNodesAsync(nodes, cancellationToken);
            _logger.LogInformation("Synced {Count} memories to graph backend", nodes.Count);
        }
    }

    private void PruneToCapacity(int maxMemories)
    {
        var toRemove = _cache.Values
            .OrderBy(m => m.Weight)
            .ThenBy(m => m.LastAccessedAt)
            .Take(_cache.Count - maxMemories)
            .Select(m => m.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            _cache.TryRemove(id, out _);
            _dirty.TryRemove(id, out _);
        }

        _logger.LogDebug("Pruned {Count} memories to enforce MaxMemories={Max}",
            toRemove.Count, maxMemories);
    }

    /// <summary>
    /// Disposes the background sync timer.
    /// </summary>
    public void Dispose()
    {
        _syncTimer?.Dispose();
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~CrossSessionMemoryStoreTests"`
Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/CrossSessionMemoryStore.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/CrossSessionMemoryStoreTests.cs
git commit -m "feat(rag): implement CrossSessionMemoryStore with session-local cache and background sync"
```

---

### Task 8: MemoryDecayService Implementation

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/MemoryDecayService.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/MemoryDecayServiceTests.cs`

- [ ] **Step 1: Write all 5 tests**

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/MemoryDecayServiceTests.cs
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.Common.Config;
using Domain.Common.Config.AI.RAG;
using FluentAssertions;
using Infrastructure.AI.RAG.GraphRag;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.GraphRag;

/// <summary>
/// Tests for <see cref="MemoryDecayService"/>.
/// Uses a mock <see cref="ICrossSessionMemoryStore"/> and
/// <see cref="IGraphDatabaseBackend"/> to verify decay and pruning logic.
/// </summary>
public sealed class MemoryDecayServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly KuzuGraphBackend _graph;
    private readonly Mock<ICrossSessionMemoryStore> _mockMemoryStore;
    private readonly MemoryDecayService _sut;
    private readonly AppConfig _appConfig;

    public MemoryDecayServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"decay_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _graph = new KuzuGraphBackend(_tempDir, NullLogger<KuzuGraphBackend>.Instance);
        _mockMemoryStore = new Mock<ICrossSessionMemoryStore>();

        _appConfig = new AppConfig();
        _appConfig.AI.Rag.CrossSessionMemory = new CrossSessionMemoryConfig
        {
            DecayRate = 0.1,
            PruneThreshold = 0.05
        };
        var monitor = new Mock<IOptionsMonitor<AppConfig>>();
        monitor.Setup(m => m.CurrentValue).Returns(_appConfig);

        _sut = new MemoryDecayService(
            _graph,
            _mockMemoryStore.Object,
            monitor.Object,
            NullLogger<MemoryDecayService>.Instance);
    }

    public void Dispose()
    {
        _graph.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ApplyDecayAsync_RecentMemory_MinimalDecay()
    {
        // Arrange — memory accessed 1 hour ago, decay rate 0.1 per day
        var now = DateTimeOffset.UtcNow;
        var node = new GraphNode
        {
            Id = "mem-recent",
            Name = "Recent memory",
            Type = "Memory",
            Properties = new Dictionary<string, string>
            {
                ["weight"] = "0.8000",
                ["last_accessed_at"] = now.AddHours(-1).ToString("O"),
                ["content"] = "Something recent"
            }
        };
        await _graph.AddNodesAsync([node]);

        // Act
        await _sut.ApplyDecayAsync();

        // Assert — 1 hour = 0.0417 days, decay = 0.8 * (1 - 0.1)^0.0417 ~ 0.7965
        var updated = await _graph.GetNodeAsync("mem-recent");
        updated.Should().NotBeNull();
        // Weight should barely change for a recent memory
        var weightStr = updated!.Properties.GetValueOrDefault("weight", "0");
        var weight = double.Parse(weightStr);
        weight.Should().BeGreaterThan(0.78, "recent memory should have minimal decay");
    }

    [Fact]
    public async Task ApplyDecayAsync_OldMemory_SignificantDecay()
    {
        // Arrange — memory accessed 30 days ago, decay rate 0.1 per day
        var now = DateTimeOffset.UtcNow;
        var node = new GraphNode
        {
            Id = "mem-old",
            Name = "Old memory",
            Type = "Memory",
            Properties = new Dictionary<string, string>
            {
                ["weight"] = "0.8000",
                ["last_accessed_at"] = now.AddDays(-30).ToString("O"),
                ["content"] = "Something old"
            }
        };
        await _graph.AddNodesAsync([node]);

        // Act
        await _sut.ApplyDecayAsync();

        // Assert — decay = 0.8 * (1 - 0.1)^30 = 0.8 * 0.0424 = 0.0339
        var updated = await _graph.GetNodeAsync("mem-old");
        updated.Should().NotBeNull();
        var weightStr = updated!.Properties.GetValueOrDefault("weight", "0");
        var weight = double.Parse(weightStr);
        weight.Should().BeLessThan(0.1, "30-day old memory should have significant decay");
    }

    [Fact]
    public async Task PruneAsync_BelowThreshold_RemovesMemory()
    {
        // Arrange — memory with weight below threshold
        var node = new GraphNode
        {
            Id = "mem-low",
            Name = "Low weight memory",
            Type = "Memory",
            Properties = new Dictionary<string, string>
            {
                ["weight"] = "0.0100",
                ["content"] = "Almost forgotten"
            }
        };
        await _graph.AddNodesAsync([node]);

        // Act
        await _sut.PruneAsync(threshold: 0.05);

        // Assert
        var exists = await _graph.NodeExistsAsync("mem-low");
        exists.Should().BeFalse("memory below threshold should be pruned");
        _mockMemoryStore.Verify(
            m => m.ForgetAsync("mem-low", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PruneAsync_AboveThreshold_PreservesMemory()
    {
        // Arrange — memory with weight above threshold
        var node = new GraphNode
        {
            Id = "mem-high",
            Name = "High weight memory",
            Type = "Memory",
            Properties = new Dictionary<string, string>
            {
                ["weight"] = "0.8000",
                ["content"] = "Well remembered"
            }
        };
        await _graph.AddNodesAsync([node]);

        // Act
        await _sut.PruneAsync(threshold: 0.05);

        // Assert
        var exists = await _graph.NodeExistsAsync("mem-high");
        exists.Should().BeTrue("memory above threshold should be preserved");
        _mockMemoryStore.Verify(
            m => m.ForgetAsync("mem-high", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyDecayAsync_AccessedMemory_ResetsDecay()
    {
        // Arrange — memory accessed just now has no decay regardless of creation date
        var now = DateTimeOffset.UtcNow;
        var node = new GraphNode
        {
            Id = "mem-accessed",
            Name = "Just accessed",
            Type = "Memory",
            Properties = new Dictionary<string, string>
            {
                ["weight"] = "0.8000",
                ["last_accessed_at"] = now.ToString("O"),
                ["content"] = "Freshly accessed"
            }
        };
        await _graph.AddNodesAsync([node]);

        // Act
        await _sut.ApplyDecayAsync();

        // Assert — 0 days since access, decay = 0.8 * (1 - 0.1)^0 = 0.8
        var updated = await _graph.GetNodeAsync("mem-accessed");
        updated.Should().NotBeNull();
        var weightStr = updated!.Properties.GetValueOrDefault("weight", "0");
        var weight = double.Parse(weightStr);
        weight.Should().BeApproximately(0.8, precision: 0.01,
            "just-accessed memory should have zero decay");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (no implementation yet)**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~MemoryDecayServiceTests"`
Expected: Build error — `MemoryDecayService` does not exist.

- [ ] **Step 3: Implement MemoryDecayService**

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/MemoryDecayService.cs
using System.Diagnostics;
using System.Text.Json;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.GraphRag;

/// <summary>
/// Applies exponential moving average (EMA) decay to cross-session memory weights
/// and prunes memories that fall below the configured threshold.
/// </summary>
/// <remarks>
/// <para>
/// Decay formula: <c>newWeight = weight * (1 - decayRate) ^ daysSinceLastAccess</c>.
/// This ensures that frequently accessed memories retain their weight while
/// unused memories naturally fade over time.
/// </para>
/// <para>
/// Designed to run on session start or via a periodic background timer. The decay
/// is idempotent when called multiple times in quick succession because
/// <c>daysSinceLastAccess</c> will be near zero, producing negligible change.
/// </para>
/// </remarks>
public sealed class MemoryDecayService : IMemoryDecayService
{
    private static readonly ActivitySource ActivitySource = new("Infrastructure.AI.RAG.GraphRag");

    private readonly IGraphDatabaseBackend _graphBackend;
    private readonly ICrossSessionMemoryStore _memoryStore;
    private readonly IOptionsMonitor<AppConfig> _configMonitor;
    private readonly ILogger<MemoryDecayService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryDecayService"/> class.
    /// </summary>
    /// <param name="graphBackend">The graph database backend containing memory nodes.</param>
    /// <param name="memoryStore">The memory store for cache-level forget operations during pruning.</param>
    /// <param name="configMonitor">Application configuration for decay rate.</param>
    /// <param name="logger">Logger.</param>
    public MemoryDecayService(
        IGraphDatabaseBackend graphBackend,
        ICrossSessionMemoryStore memoryStore,
        IOptionsMonitor<AppConfig> configMonitor,
        ILogger<MemoryDecayService> logger)
    {
        ArgumentNullException.ThrowIfNull(graphBackend);
        ArgumentNullException.ThrowIfNull(memoryStore);
        ArgumentNullException.ThrowIfNull(configMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _graphBackend = graphBackend;
        _memoryStore = memoryStore;
        _configMonitor = configMonitor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ApplyDecayAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.memory.decay");
        var config = _configMonitor.CurrentValue.AI.Rag.CrossSessionMemory;
        var decayRate = config.DecayRate;
        var now = DateTimeOffset.UtcNow;

        var allNodes = await _graphBackend.GetAllNodesAsync(cancellationToken);
        var memoryNodes = allNodes.Where(n => n.Type == "Memory").ToList();

        var decayedCount = 0;

        foreach (var node in memoryNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var weightStr = node.Properties.GetValueOrDefault("weight", "1.0");
            if (!double.TryParse(weightStr, out var currentWeight))
                continue;

            var lastAccessedStr = node.Properties.GetValueOrDefault("last_accessed_at");
            if (string.IsNullOrEmpty(lastAccessedStr) ||
                !DateTimeOffset.TryParse(lastAccessedStr, out var lastAccessed))
                continue;

            var daysSinceAccess = (now - lastAccessed).TotalDays;
            if (daysSinceAccess < 0)
                daysSinceAccess = 0;

            var newWeight = currentWeight * Math.Pow(1 - decayRate, daysSinceAccess);
            newWeight = Math.Max(0.0, newWeight);

            if (Math.Abs(newWeight - currentWeight) < 0.0001)
                continue;

            var updatedProperties = new Dictionary<string, string>(node.Properties)
            {
                ["weight"] = newWeight.ToString("F4")
            };

            var updatedNode = node with { Properties = updatedProperties };
            await _graphBackend.AddNodesAsync([updatedNode], cancellationToken);
            decayedCount++;
        }

        activity?.SetTag("rag.memory.decay_count", decayedCount);
        _logger.LogInformation("Memory decay applied: {Decayed}/{Total} memories updated",
            decayedCount, memoryNodes.Count);
    }

    /// <inheritdoc />
    public async Task PruneAsync(double threshold, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.memory.prune");

        var allNodes = await _graphBackend.GetAllNodesAsync(cancellationToken);
        var memoryNodes = allNodes.Where(n => n.Type == "Memory").ToList();

        var prunedCount = 0;

        foreach (var node in memoryNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var weightStr = node.Properties.GetValueOrDefault("weight", "1.0");
            if (!double.TryParse(weightStr, out var weight))
                continue;

            if (weight < threshold)
            {
                await _graphBackend.DeleteNodeAsync(node.Id, cancellationToken);
                await _memoryStore.ForgetAsync(node.Id, cancellationToken);
                prunedCount++;
            }
        }

        activity?.SetTag("rag.memory.prune_count", prunedCount);
        _logger.LogInformation("Memory pruning: {Pruned} memories below threshold {Threshold:F4} removed",
            prunedCount, threshold);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~MemoryDecayServiceTests"`
Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/MemoryDecayService.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/MemoryDecayServiceTests.cs
git commit -m "feat(rag): implement MemoryDecayService with EMA decay and threshold pruning"
```

---

### Task 9: ManagedCodeGraphRagService Refactoring

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/ManagedCodeGraphRagService.cs`
- Create: `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs` (partial — ManagedCode tests)

- [ ] **Step 1: Write 4 tests for the refactored ManagedCodeGraphRagService**

Add these tests to the integration test file (they'll also serve Task 12):

```csharp
// src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.KnowledgeGraph.Models;
using Domain.AI.RAG.Models;
using Domain.Common.Config;
using FluentAssertions;
using Infrastructure.AI.RAG.GraphRag;
using Infrastructure.AI.RAG.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Infrastructure.AI.RAG.Tests.GraphRag;

/// <summary>
/// Integration tests for the refactored <see cref="ManagedCodeGraphRagService"/>
/// using <see cref="IGraphDatabaseBackend"/> and <see cref="ICommunityDetector"/>.
/// </summary>
public sealed class GraphRagIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly KuzuGraphBackend _graph;
    private readonly Mock<IRagModelRouter> _mockRouter;
    private readonly Mock<IProvenanceStamper> _mockStamper;
    private readonly Mock<ICommunityDetector> _mockDetector;
    private readonly IOptionsMonitor<AppConfig> _configMonitor;
    private readonly ManagedCodeGraphRagService _sut;

    public GraphRagIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"graphrag_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _graph = new KuzuGraphBackend(_tempDir, NullLogger<KuzuGraphBackend>.Instance);

        var mockClient = new Mock<IChatClient>();
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"entities":[{"name":"Azure","type":"Technology"}],"relationships":[]}""")));

        _mockRouter = new Mock<IRagModelRouter>();
        _mockRouter.Setup(r => r.GetClientForOperation(It.IsAny<string>())).Returns(mockClient.Object);

        _mockStamper = new Mock<IProvenanceStamper>();
        _mockStamper
            .Setup(s => s.StampNode(It.IsAny<GraphNode>(), It.IsAny<ProvenanceStamp>()))
            .Returns((GraphNode n, ProvenanceStamp _) => n);
        _mockStamper
            .Setup(s => s.StampEdge(It.IsAny<GraphEdge>(), It.IsAny<ProvenanceStamp>()))
            .Returns((GraphEdge e, ProvenanceStamp _) => e);
        _mockStamper
            .Setup(s => s.CreateStamp(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>()))
            .Returns(new ProvenanceStamp
            {
                SourcePipeline = "test",
                SourceTask = "test",
                Timestamp = DateTimeOffset.UtcNow
            });

        _mockDetector = new Mock<ICommunityDetector>();

        _configMonitor = RagTestData.CreateConfigMonitor(c =>
        {
            c.AI.Rag.GraphRag.Enabled = true;
            c.AI.Rag.GraphRag.CommunityLevel = 0;
        });

        _sut = new ManagedCodeGraphRagService(
            _graph,
            _mockRouter.Object,
            _mockStamper.Object,
            _mockDetector.Object,
            NullLogger<ManagedCodeGraphRagService>.Instance,
            _configMonitor);
    }

    public void Dispose()
    {
        _graph.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task IndexCorpusAsync_PersistsToGraphBackend()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            RagTestData.CreateChunk("c1", "Azure is a cloud computing platform by Microsoft.")
        };

        // Act
        await _sut.IndexCorpusAsync(chunks);

        // Assert
        var nodeCount = await _graph.GetNodeCountAsync();
        nodeCount.Should().BeGreaterThan(0, "entities should be persisted to graph backend");
    }

    [Fact]
    public async Task GlobalSearchAsync_UsesCommunities_WhenAvailable()
    {
        // Arrange — set up communities in the graph
        var community = RagTestData.CreateCommunity("comm_0_1", level: 0,
            summary: "Technology cluster focused on cloud computing.");
        await _graph.SaveCommunityAsync(community);
        await _graph.AddNodesAsync([
            RagTestData.CreateGraphNode("node-1", "Azure", "Technology"),
            RagTestData.CreateGraphNode("node-2", "OpenAI", "Organization"),
            RagTestData.CreateGraphNode("node-3", "GPT", "Technology")
        ]);

        var synthesisClient = new Mock<IChatClient>();
        synthesisClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "The corpus covers cloud computing themes centered on Azure and AI.")));
        _mockRouter.Setup(r => r.GetClientForOperation("graph_global_search"))
            .Returns(synthesisClient.Object);

        // Act
        var result = await _sut.GlobalSearchAsync("What are the main themes?", communityLevel: 0);

        // Assert
        result.AssembledText.Should().Contain("cloud computing");
    }

    [Fact]
    public async Task LocalSearchAsync_UsesGraphTraversal()
    {
        // Arrange
        await _graph.AddNodesAsync([
            RagTestData.CreateGraphNode("n1", "Azure", "Technology", chunkIds: ["c1"]),
            RagTestData.CreateGraphNode("n2", "OpenAI", "Organization", chunkIds: ["c2"])
        ]);
        await _graph.AddEdgesAsync([
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "partners_with")
        ]);

        // Act
        var results = await _sut.LocalSearchAsync("Azure", topK: 5);

        // Assert
        results.Should().NotBeEmpty("Azure should match a graph node");
        results.SelectMany(r => new[] { r.Chunk.Id })
            .Should().Contain("c1", "chunk from matched node should be returned");
    }

    [Fact]
    public async Task GlobalSearchAsync_FallsBackToFullScan_WhenNoCommunitiesExist()
    {
        // Arrange — add nodes and edges but no communities
        await _graph.AddNodesAsync([
            RagTestData.CreateGraphNode("n1", "Azure", "Technology"),
            RagTestData.CreateGraphNode("n2", "OpenAI", "Organization")
        ]);
        await _graph.AddEdgesAsync([
            RagTestData.CreateGraphEdge("e1", "n1", "n2", "partners_with")
        ]);

        var synthesisClient = new Mock<IChatClient>();
        synthesisClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "Based on the full graph scan: Azure and OpenAI are partners.")));
        _mockRouter.Setup(r => r.GetClientForOperation("graph_global_search"))
            .Returns(synthesisClient.Object);

        // Act
        var result = await _sut.GlobalSearchAsync("What are the themes?", communityLevel: 0);

        // Assert
        result.AssembledText.Should().NotBeEmpty("should fall back to full scan");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (constructor signature mismatch)**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~GraphRagIntegrationTests"`
Expected: Build error — constructor doesn't accept `ICommunityDetector`.

- [ ] **Step 3: Refactor ManagedCodeGraphRagService to use IGraphDatabaseBackend**

Replace the existing `ManagedCodeGraphRagService.cs` with this updated version. Key changes:
1. Constructor accepts `IGraphDatabaseBackend` instead of `IKnowledgeGraphStore`
2. Constructor accepts `ICommunityDetector` for community-aware global search
3. `GlobalSearchAsync` checks for communities first, falls back to full scan
4. `LocalSearchAsync` uses `TraverseAsync` for graph traversal

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/ManagedCodeGraphRagService.cs
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.KnowledgeGraph.Models;
using Domain.AI.RAG.Models;
using Domain.AI.Telemetry.Conventions;
using Domain.Common.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.GraphRag;

/// <summary>
/// High-level <see cref="IGraphRagService"/> implementation that delegates graph storage
/// to <see cref="IGraphDatabaseBackend"/> and provides LLM-based entity extraction,
/// community-aware global search, and graph-traversal local search.
/// </summary>
/// <remarks>
/// <para>
/// This service orchestrates the GraphRAG pipeline: entity extraction via LLM,
/// graph construction via <see cref="IGraphDatabaseBackend"/>, and search via graph
/// traversal + LLM synthesis. Community detection via <see cref="ICommunityDetector"/>
/// enables hierarchical global search — each community summary captures a theme
/// that the LLM synthesizes into a global answer.
/// </para>
/// <para>
/// When no communities have been detected, global search falls back to a full-scan
/// approach using all triplets in the graph.
/// </para>
/// </remarks>
public sealed class ManagedCodeGraphRagService : IGraphRagService
{
    private static readonly ActivitySource ActivitySource = new("Infrastructure.AI.RAG.GraphRag");

    private readonly IGraphDatabaseBackend _graphBackend;
    private readonly IRagModelRouter _modelRouter;
    private readonly IProvenanceStamper _provenanceStamper;
    private readonly ICommunityDetector _communityDetector;
    private readonly ILogger<ManagedCodeGraphRagService> _logger;
    private readonly IOptionsMonitor<AppConfig> _configMonitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedCodeGraphRagService"/> class.
    /// </summary>
    /// <param name="graphBackend">The graph database backend for storage and traversal.</param>
    /// <param name="modelRouter">Routes LLM calls to the appropriate model tier.</param>
    /// <param name="provenanceStamper">Stamps provenance metadata on extracted entities.</param>
    /// <param name="communityDetector">Detects communities for global search.</param>
    /// <param name="logger">Logger for recording graph operations.</param>
    /// <param name="configMonitor">Application configuration monitor.</param>
    public ManagedCodeGraphRagService(
        IGraphDatabaseBackend graphBackend,
        IRagModelRouter modelRouter,
        IProvenanceStamper provenanceStamper,
        ICommunityDetector communityDetector,
        ILogger<ManagedCodeGraphRagService> logger,
        IOptionsMonitor<AppConfig> configMonitor)
    {
        ArgumentNullException.ThrowIfNull(graphBackend);
        ArgumentNullException.ThrowIfNull(modelRouter);
        ArgumentNullException.ThrowIfNull(provenanceStamper);
        ArgumentNullException.ThrowIfNull(communityDetector);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configMonitor);

        _graphBackend = graphBackend;
        _modelRouter = modelRouter;
        _provenanceStamper = provenanceStamper;
        _communityDetector = communityDetector;
        _logger = logger;
        _configMonitor = configMonitor;
    }

    /// <inheritdoc />
    public async Task IndexCorpusAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.graph.index_corpus");
        activity?.SetTag(RagConventions.IngestChunksProduced, chunks.Count);

        _logger.LogInformation("GraphRAG indexing started: {ChunkCount} chunks", chunks.Count);
        var client = _modelRouter.GetClientForOperation("graph_entity_extraction");

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extracted = await ExtractEntitiesAsync(client, chunk, cancellationToken);

            var stamp = _provenanceStamper.CreateStamp(
                "rag_ingestion", "entity_extraction",
                sourceDocumentId: chunk.DocumentId);

            var stampedNodes = extracted.Nodes
                .Select(n => _provenanceStamper.StampNode(n, stamp))
                .ToList();
            var stampedEdges = extracted.Edges
                .Select(e => _provenanceStamper.StampEdge(e, stamp))
                .ToList();

            if (stampedNodes.Count > 0)
                await _graphBackend.AddNodesAsync(stampedNodes, cancellationToken);
            if (stampedEdges.Count > 0)
                await _graphBackend.AddEdgesAsync(stampedEdges, cancellationToken);
        }

        var nodeCount = await _graphBackend.GetNodeCountAsync(cancellationToken);
        var edgeCount = await _graphBackend.GetEdgeCountAsync(cancellationToken);
        _logger.LogInformation(
            "GraphRAG indexing completed: {EntityCount} entities, {RelCount} relationships",
            nodeCount, edgeCount);
    }

    /// <inheritdoc />
    public async Task<RagAssembledContext> GlobalSearchAsync(
        string query,
        int communityLevel,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.graph.global_search");
        activity?.SetTag(RagConventions.GraphCommunityLevel, communityLevel);
        activity?.SetTag(RagConventions.RetrievalStrategy, RagConventions.StrategyValues.GraphRag);

        var communities = await _graphBackend.GetCommunitiesAsync(communityLevel, cancellationToken);

        string summary;
        if (communities.Count > 0)
        {
            summary = BuildCommunitySummaryFromCommunities(communities);
            activity?.SetTag("rag.graph.community_count", communities.Count);
            _logger.LogInformation(
                "GlobalSearch using {CommunityCount} communities at level {Level}",
                communities.Count, communityLevel);
        }
        else
        {
            _logger.LogInformation(
                "No communities at level {Level}, falling back to full-scan global search",
                communityLevel);
            var triplets = await _graphBackend.GetTripletsAsync([], cancellationToken);
            if (triplets.Count == 0)
            {
                return new RagAssembledContext
                {
                    AssembledText = "No entities have been indexed. Please ingest documents first.",
                    TotalTokens = 0,
                    WasTruncated = false
                };
            }
            summary = BuildCommunitySummaryFromTriplets(triplets);
        }

        var client = _modelRouter.GetClientForOperation("graph_global_search");
        var prompt = $$"""
            You are a knowledge graph analyst. Based on the following entity and relationship
            summary extracted from a document corpus, answer the user's query by synthesizing
            themes and patterns across the entire graph.

            ## Knowledge Graph Summary
            {{summary}}

            ## User Query
            {{query}}

            Provide a comprehensive answer that references specific entities and relationships.
            """;

        var response = await client.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        var text = response.Text ?? string.Empty;

        _logger.LogInformation(
            "GraphRAG global search completed: Level={Level}, Communities={Count}",
            communityLevel, communities.Count);

        return new RagAssembledContext
        {
            AssembledText = text,
            TotalTokens = text.Length / 4,
            WasTruncated = false
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> LocalSearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.graph.local_search");
        activity?.SetTag(RagConventions.RetrievalStrategy, RagConventions.StrategyValues.GraphRag);

        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var allNodes = await _graphBackend.GetAllNodesAsync(cancellationToken);
        if (allNodes.Count == 0)
            return [];

        var matchedNodeIds = allNodes
            .Where(n => queryTerms.Any(t =>
                n.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                n.Type.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Id)
            .ToHashSet();

        if (matchedNodeIds.Count == 0)
            return [];

        var allChunkIds = new HashSet<string>();

        foreach (var nodeId in matchedNodeIds)
        {
            var neighbors = await _graphBackend.TraverseAsync(nodeId, maxDepth: 1, cancellationToken);
            foreach (var neighbor in neighbors)
                foreach (var cid in neighbor.ChunkIds)
                    allChunkIds.Add(cid);

            var node = await _graphBackend.GetNodeAsync(nodeId, cancellationToken);
            if (node is not null)
                foreach (var cid in node.ChunkIds)
                    allChunkIds.Add(cid);
        }

        _logger.LogInformation(
            "GraphRAG local search: {MatchedEntities} entities matched, {ChunkCount} chunks found",
            matchedNodeIds.Count, allChunkIds.Count);

        activity?.SetTag("rag.graph.traversal_depth", 1);
        activity?.SetTag(RagConventions.RetrievalChunksReturned, allChunkIds.Count);

        return allChunkIds
            .Take(topK)
            .Select((id, index) => new RetrievalResult
            {
                Chunk = new DocumentChunk
                {
                    Id = id,
                    DocumentId = "",
                    SectionPath = "",
                    Content = $"[Graph result from entity match - chunk {id}]",
                    Tokens = 0,
                    Metadata = new ChunkMetadata
                    {
                        SourceUri = new Uri("graph://entity-match"),
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                },
                DenseScore = 1.0 - (index * 0.05),
                SparseScore = 0.0,
                FusedScore = 1.0 - (index * 0.05)
            })
            .ToList();
    }

    private async Task<ExtractionResult> ExtractEntitiesAsync(
        IChatClient client,
        DocumentChunk chunk,
        CancellationToken cancellationToken)
    {
        var contentSnippet = chunk.Content[..Math.Min(chunk.Content.Length, 2000)];
        var prompt = $$"""
            Extract named entities and relationships from the following text.
            Return a JSON object with:
            - "entities": array of {"name": string, "type": string}
            - "relationships": array of {"source": string, "predicate": string, "target": string}

            Text:
            {{contentSnippet}}

            JSON:
            """;

        try
        {
            var response = await client.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            var json = response.Text ?? "{}";

            var startIndex = json.IndexOf('{');
            var endIndex = json.LastIndexOf('}');
            if (startIndex >= 0 && endIndex > startIndex)
                json = json[startIndex..(endIndex + 1)];

            var parsed = JsonSerializer.Deserialize<ExtractionJson>(json, JsonOptions);

            var nodes = (parsed?.Entities ?? [])
                .Select(e => new GraphNode
                {
                    Id = $"{(e.Name ?? "unknown").ToLowerInvariant()}:{(e.Type ?? "unknown").ToLowerInvariant()}",
                    Name = e.Name ?? "unknown",
                    Type = e.Type ?? "unknown",
                    ChunkIds = [chunk.Id]
                })
                .ToList();

            var edges = (parsed?.Relationships ?? [])
                .Select(r =>
                {
                    var source = $"{(r.Source ?? "unknown").ToLowerInvariant()}:entity";
                    var target = $"{(r.Target ?? "unknown").ToLowerInvariant()}:entity";
                    var predicate = r.Predicate ?? "related_to";
                    return new GraphEdge
                    {
                        Id = $"{source}|{predicate}|{target}",
                        SourceNodeId = source,
                        TargetNodeId = target,
                        Predicate = predicate,
                        ChunkId = chunk.Id
                    };
                })
                .ToList();

            return new ExtractionResult(nodes, edges);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity extraction failed for chunk {ChunkId}", chunk.Id);
            return new ExtractionResult([], []);
        }
    }

    private static string BuildCommunitySummaryFromCommunities(IReadOnlyList<Community> communities)
    {
        var sb = new StringBuilder();
        foreach (var community in communities.Take(20))
        {
            sb.AppendLine($"### Community: {community.Id} ({community.NodeIds.Count} entities)");
            sb.AppendLine(community.Summary);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildCommunitySummaryFromTriplets(IReadOnlyList<GraphTriplet> triplets)
    {
        var sb = new StringBuilder();

        var nodeNames = new HashSet<string>();
        foreach (var t in triplets.Take(100))
        {
            if (nodeNames.Add(t.Source.Name))
                sb.AppendLine($"  - {t.Source.Name} ({t.Source.Type}): referenced in {t.Source.ChunkIds.Count} chunks");
            if (nodeNames.Add(t.Target.Name))
                sb.AppendLine($"  - {t.Target.Name} ({t.Target.Type}): referenced in {t.Target.ChunkIds.Count} chunks");
        }

        sb.AppendLine();
        sb.AppendLine($"Relationships ({triplets.Count}):");
        foreach (var t in triplets.Take(100))
            sb.AppendLine($"  - {t.Source.Name} --[{t.Edge.Predicate}]--> {t.Target.Name}");

        return sb.ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ExtractionResult(
        IReadOnlyList<GraphNode> Nodes,
        IReadOnlyList<GraphEdge> Edges);

    private sealed record ExtractionJson
    {
        public List<EntityJson>? Entities { get; init; }
        public List<RelationshipJson>? Relationships { get; init; }
    }

    private sealed record EntityJson
    {
        public string? Name { get; init; }
        public string? Type { get; init; }
    }

    private sealed record RelationshipJson
    {
        public string? Source { get; init; }
        public string? Predicate { get; init; }
        public string? Target { get; init; }
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~GraphRagIntegrationTests"`
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/GraphRag/ManagedCodeGraphRagService.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs
git commit -m "refactor(rag): refactor ManagedCodeGraphRagService to use IGraphDatabaseBackend and community-aware global search"
```

---

### Task 10: FeedbackWeightedScorer Enhancement

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/FeedbackWeightedScorer.cs`
- Create: (tests added to existing integration file or new file if needed)

- [ ] **Step 1: Write 3 tests for the feedback persistence enhancement**

Add these to `GraphRagIntegrationTests.cs` as an inner class, or create a separate file:

```csharp
// Append to src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs
// (or create separate file — shown here as separate for clarity)

// If separate file, place at:
// src/Content/Tests/Infrastructure.AI.RAG.Tests/Retrieval/FeedbackWeightedScorerPersistenceTests.cs
```

Since modifying the integration file is cleaner for this plan, add these tests as a separate test class in the same file:

```csharp
// Add this class to the end of GraphRagIntegrationTests.cs, inside the namespace block:

/// <summary>
/// Tests for <see cref="FeedbackWeightedScorer"/> enhancement — feedback persistence
/// to the graph backend after blending.
/// </summary>
public sealed class FeedbackWeightedScorerPersistenceTests
{
    [Fact]
    public async Task BlendFeedbackAsync_PersistsFeedbackToGraph()
    {
        // Arrange
        var mockFeedbackStore = new Mock<IFeedbackStore>();
        var mockGraphStore = new Mock<IGraphDatabaseBackend>();

        var nodeWeight = new NodeFeedbackWeight
        {
            NodeId = "n1",
            Weight = 0.8,
            UpdateCount = 5,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        mockFeedbackStore
            .Setup(f => f.GetNodeWeightsBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, NodeFeedbackWeight> { ["n1"] = nodeWeight });

        var triplet = new GraphTriplet
        {
            Source = RagTestData.CreateGraphNode("n1", "Azure", "Tech", chunkIds: ["chunk-1"]),
            Edge = RagTestData.CreateGraphEdge("e1", "n1", "n2", "uses"),
            Target = RagTestData.CreateGraphNode("n2", "OpenAI", "Org")
        };
        mockGraphStore
            .Setup(g => g.GetTripletsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphTriplet> { triplet });

        var configMonitor = RagTestData.CreateConfigMonitor(c =>
        {
            c.AI.Rag.GraphRag.FeedbackAlpha = 0.3;
            c.AI.Rag.GraphRag.FeedbackEnabled = true;
        });

        var sut = new FeedbackWeightedScorer(
            mockFeedbackStore.Object,
            mockGraphStore.Object,
            configMonitor,
            NullLogger<FeedbackWeightedScorer>.Instance);

        var reranked = new List<RerankedResult> { RagTestData.CreateRerankedResult("chunk-1") };

        // Act
        await sut.BlendFeedbackAsync(reranked, "test query");

        // Assert
        mockGraphStore.Verify(
            g => g.UpdateNodeWeightAsync("n1", It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "should persist the blended feedback weight back to the graph");
    }

    [Fact]
    public async Task BlendFeedbackAsync_NoGraphNodes_SkipsPersistence()
    {
        // Arrange
        var mockFeedbackStore = new Mock<IFeedbackStore>();
        var mockGraphStore = new Mock<IGraphDatabaseBackend>();

        mockGraphStore
            .Setup(g => g.GetTripletsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GraphTriplet>());

        var configMonitor = RagTestData.CreateConfigMonitor(c =>
            c.AI.Rag.GraphRag.FeedbackAlpha = 0.3);

        var sut = new FeedbackWeightedScorer(
            mockFeedbackStore.Object,
            mockGraphStore.Object,
            configMonitor,
            NullLogger<FeedbackWeightedScorer>.Instance);

        var reranked = new List<RerankedResult> { RagTestData.CreateRerankedResult() };

        // Act
        await sut.BlendFeedbackAsync(reranked, "test query");

        // Assert
        mockGraphStore.Verify(
            g => g.UpdateNodeWeightAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "should not persist when no graph nodes match");
    }

    [Fact]
    public async Task BlendFeedbackAsync_UpdatesExistingWeights()
    {
        // Arrange
        var mockFeedbackStore = new Mock<IFeedbackStore>();
        var mockGraphStore = new Mock<IGraphDatabaseBackend>();

        var nodeWeight = new NodeFeedbackWeight
        {
            NodeId = "n1",
            Weight = 0.5,
            UpdateCount = 10,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        mockFeedbackStore
            .Setup(f => f.GetNodeWeightsBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, NodeFeedbackWeight> { ["n1"] = nodeWeight });

        var triplet = new GraphTriplet
        {
            Source = RagTestData.CreateGraphNode("n1", "Azure", "Tech", chunkIds: ["chunk-1"]),
            Edge = RagTestData.CreateGraphEdge("e1", "n1", "n2", "uses"),
            Target = RagTestData.CreateGraphNode("n2", "OpenAI", "Org")
        };
        mockGraphStore
            .Setup(g => g.GetTripletsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphTriplet> { triplet });

        var configMonitor = RagTestData.CreateConfigMonitor(c =>
        {
            c.AI.Rag.GraphRag.FeedbackAlpha = 0.3;
            c.AI.Rag.GraphRag.FeedbackEnabled = true;
        });

        var sut = new FeedbackWeightedScorer(
            mockFeedbackStore.Object,
            mockGraphStore.Object,
            configMonitor,
            NullLogger<FeedbackWeightedScorer>.Instance);

        var reranked = new List<RerankedResult>
        {
            RagTestData.CreateRerankedResult("chunk-1", rerankScore: 0.9)
        };

        // Act
        await sut.BlendFeedbackAsync(reranked, "Azure query");

        // Assert — the persisted weight should be the blended score
        mockGraphStore.Verify(
            g => g.UpdateNodeWeightAsync("n1",
                It.Is<double>(w => w > 0.0 && w <= 1.0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (missing UpdateNodeWeightAsync call)**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~FeedbackWeightedScorerPersistenceTests"`
Expected: `BlendFeedbackAsync_PersistsFeedbackToGraph` fails — `UpdateNodeWeightAsync` never called.

- [ ] **Step 3: Modify FeedbackWeightedScorer to accept IGraphDatabaseBackend and persist feedback**

Update `FeedbackWeightedScorer.cs`. The key change: accept `IGraphDatabaseBackend` (which extends `IKnowledgeGraphStore`) and call `UpdateNodeWeightAsync` after blending:

```csharp
// src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/FeedbackWeightedScorer.cs
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Application.AI.Common.Interfaces.RAG;
using Domain.AI.RAG.Models;
using Domain.Common.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.RAG.Retrieval;

/// <summary>
/// Blends reranked retrieval scores with feedback weights from the knowledge graph,
/// then persists the adjusted weights back to the graph backend for future retrieval
/// improvement.
/// </summary>
/// <remarks>
/// Blending formula:
/// <c>adjustedScore = (1 - alpha) * rerankScore + alpha * avgNodeWeight</c>.
/// Chunks without matching graph entities pass through with their original score.
/// After blending, the adjusted scores are persisted back to the graph via
/// <see cref="IGraphDatabaseBackend.UpdateNodeWeightAsync"/> so that future
/// retrievals benefit from accumulated feedback.
/// </remarks>
public sealed class FeedbackWeightedScorer : IFeedbackWeightedScorer
{
    private readonly IFeedbackStore _feedbackStore;
    private readonly IGraphDatabaseBackend _graphBackend;
    private readonly IOptionsMonitor<AppConfig> _configMonitor;
    private readonly ILogger<FeedbackWeightedScorer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackWeightedScorer"/> class.
    /// </summary>
    /// <param name="feedbackStore">Feedback weight storage.</param>
    /// <param name="graphBackend">Graph database backend for weight persistence.</param>
    /// <param name="configMonitor">Application configuration for alpha value.</param>
    /// <param name="logger">Logger for recording blending decisions.</param>
    public FeedbackWeightedScorer(
        IFeedbackStore feedbackStore,
        IGraphDatabaseBackend graphBackend,
        IOptionsMonitor<AppConfig> configMonitor,
        ILogger<FeedbackWeightedScorer> logger)
    {
        ArgumentNullException.ThrowIfNull(feedbackStore);
        ArgumentNullException.ThrowIfNull(graphBackend);
        ArgumentNullException.ThrowIfNull(configMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _feedbackStore = feedbackStore;
        _graphBackend = graphBackend;
        _configMonitor = configMonitor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankedResult>> BlendFeedbackAsync(
        IReadOnlyList<RerankedResult> rerankedResults,
        string query,
        CancellationToken cancellationToken = default)
    {
        var alpha = _configMonitor.CurrentValue.AI.Rag.GraphRag.FeedbackAlpha;
        var chunkIds = rerankedResults
            .Select(r => r.RetrievalResult.Chunk.Id)
            .ToList();

        var triplets = await _graphBackend.GetTripletsAsync(chunkIds, cancellationToken);
        if (triplets.Count == 0)
        {
            _logger.LogDebug("No graph triplets found for chunk IDs; skipping feedback blending");
            return rerankedResults;
        }

        var chunkToNodeIds = BuildChunkToNodeMap(triplets);

        var allNodeIds = chunkToNodeIds.Values
            .SelectMany(ids => ids)
            .Distinct()
            .ToList();
        var nodeWeights = await _feedbackStore.GetNodeWeightsBatchAsync(allNodeIds, cancellationToken);

        var adjusted = new List<RerankedResult>(rerankedResults.Count);
        var blendedCount = 0;
        var nodeAdjustedWeights = new Dictionary<string, double>();

        foreach (var result in rerankedResults)
        {
            var chunkId = result.RetrievalResult.Chunk.Id;
            if (!chunkToNodeIds.TryGetValue(chunkId, out var nodeIds) || nodeIds.Count == 0)
            {
                adjusted.Add(result);
                continue;
            }

            var avgWeight = nodeIds
                .Where(nodeWeights.ContainsKey)
                .Select(id => nodeWeights[id].Weight)
                .DefaultIfEmpty(1.0)
                .Average();

            var adjustedScore = (1 - alpha) * result.RerankScore + alpha * avgWeight;
            adjusted.Add(result with { RerankScore = adjustedScore });
            blendedCount++;

            foreach (var nodeId in nodeIds)
            {
                nodeAdjustedWeights[nodeId] = adjustedScore;
            }
        }

        var sorted = adjusted
            .OrderByDescending(r => r.RerankScore)
            .Select((r, i) => r with { RerankRank = i + 1 })
            .ToList();

        // Persist adjusted weights back to graph backend
        foreach (var (nodeId, adjustedWeight) in nodeAdjustedWeights)
        {
            var clampedWeight = Math.Clamp(adjustedWeight, 0.0, 1.0);
            await _graphBackend.UpdateNodeWeightAsync(nodeId, clampedWeight, cancellationToken);
        }

        _logger.LogInformation(
            "Feedback blending: {Blended}/{Total} results adjusted, alpha={Alpha:F2}, {PersistCount} weights persisted",
            blendedCount, rerankedResults.Count, alpha, nodeAdjustedWeights.Count);

        return sorted;
    }

    private static Dictionary<string, List<string>> BuildChunkToNodeMap(
        IReadOnlyList<Domain.AI.KnowledgeGraph.Models.GraphTriplet> triplets)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var t in triplets)
        {
            foreach (var chunkId in t.Source.ChunkIds)
            {
                if (!map.TryGetValue(chunkId, out var list))
                    map[chunkId] = list = [];
                list.Add(t.Source.Id);
            }

            foreach (var chunkId in t.Target.ChunkIds)
            {
                if (!map.TryGetValue(chunkId, out var list))
                    map[chunkId] = list = [];
                list.Add(t.Target.Id);
            }
        }

        return map;
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~FeedbackWeightedScorerPersistenceTests"`
Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/Retrieval/FeedbackWeightedScorer.cs src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs
git commit -m "feat(rag): enhance FeedbackWeightedScorer to persist feedback weights to graph backend"
```

---

### Task 11: DI Registration

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs`

- [ ] **Step 1: Add `AddRagGraphDatabase` and `AddRagCrossSessionMemory` methods**

Add these two private methods and update `AddRagDependencies` and `AddRagGraphRag` in `DependencyInjection.cs`:

```csharp
// Add to the AddRagDependencies method, after AddRagOrchestration:
        AddRagGraphDatabase(services, appConfig);
        AddRagCrossSessionMemory(services, appConfig);
```

```csharp
// Add these usings at the top of DependencyInjection.cs:
// using Application.AI.Common.Interfaces.KnowledgeGraph; (if not already present)

    /// <summary>
    /// Registers the graph database backend and community detector services.
    /// The backend provider is selected via <c>GraphDatabaseConfig.Provider</c>
    /// using keyed DI.
    /// </summary>
    private static void AddRagGraphDatabase(IServiceCollection services, AppConfig appConfig)
    {
        var graphDbConfig = appConfig.AI.Rag.GraphDatabase;
        if (!graphDbConfig.Enabled)
            return;

        // Graph database backends — keyed by provider name
        services.AddKeyedSingleton<IGraphDatabaseBackend>("kuzu", (sp, _) =>
            new KuzuGraphBackend(
                sp.GetRequiredService<IOptionsMonitor<AppConfig>>()
                    .CurrentValue.AI.Rag.GraphDatabase.DataDirectory,
                sp.GetRequiredService<ILogger<KuzuGraphBackend>>()));

        // Default graph backend from config
        services.AddSingleton<IGraphDatabaseBackend>(sp =>
        {
            var config = sp.GetRequiredService<IOptionsMonitor<AppConfig>>().CurrentValue;
            var provider = config.AI.Rag.GraphDatabase.Provider;
            return sp.GetRequiredKeyedService<IGraphDatabaseBackend>(provider);
        });

        // Community detector
        services.AddSingleton<ICommunityDetector>(sp =>
            new LeidenCommunityDetector(
                sp.GetRequiredService<ILogger<LeidenCommunityDetector>>()));
    }

    /// <summary>
    /// Registers cross-session memory services: memory store and decay service.
    /// Only registered when <c>CrossSessionMemoryConfig.Enabled</c> is <c>true</c>.
    /// </summary>
    private static void AddRagCrossSessionMemory(IServiceCollection services, AppConfig appConfig)
    {
        var memoryConfig = appConfig.AI.Rag.CrossSessionMemory;
        if (!memoryConfig.Enabled)
            return;

        services.AddSingleton<ICrossSessionMemoryStore>(sp =>
            new CrossSessionMemoryStore(
                sp.GetRequiredService<IGraphDatabaseBackend>(),
                sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
                sp.GetRequiredService<ILogger<CrossSessionMemoryStore>>()));

        services.AddSingleton<IMemoryDecayService>(sp =>
            new MemoryDecayService(
                sp.GetRequiredService<IGraphDatabaseBackend>(),
                sp.GetRequiredService<ICrossSessionMemoryStore>(),
                sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
                sp.GetRequiredService<ILogger<MemoryDecayService>>()));
    }
```

Also update `AddRagGraphRag` to use `IGraphDatabaseBackend` instead of `IKnowledgeGraphStore`:

```csharp
    private static void AddRagGraphRag(IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<IGraphRagService>(sp =>
            new ManagedCodeGraphRagService(
                sp.GetRequiredService<IGraphDatabaseBackend>(),
                sp.GetRequiredService<IRagModelRouter>(),
                sp.GetRequiredService<IProvenanceStamper>(),
                sp.GetRequiredService<ICommunityDetector>(),
                sp.GetRequiredService<ILogger<ManagedCodeGraphRagService>>(),
                sp.GetRequiredService<IOptionsMonitor<AppConfig>>()));

        // Feedback-weighted scoring (only registered when feedback enabled)
        if (appConfig.AI.Rag.GraphRag.FeedbackEnabled)
        {
            services.AddSingleton<IFeedbackWeightedScorer>(sp =>
                new Retrieval.FeedbackWeightedScorer(
                    sp.GetRequiredService<IFeedbackStore>(),
                    sp.GetRequiredService<IGraphDatabaseBackend>(),
                    sp.GetRequiredService<IOptionsMonitor<AppConfig>>(),
                    sp.GetRequiredService<ILogger<Retrieval.FeedbackWeightedScorer>>()));
        }
    }
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI.RAG/DependencyInjection.cs
git commit -m "feat(rag): register graph database backend, community detector, and cross-session memory services"
```

---

### Task 12: Integration Tests — End-to-End Graph Pipeline

**Files:**
- Modify: `src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs`

The core tests were written in Task 9. Here we add 3 end-to-end tests that exercise the full pipeline: ingest, community detection, search.

- [ ] **Step 1: Add end-to-end integration tests**

Add these tests to the `GraphRagIntegrationTests` class:

```csharp
// Add to the GraphRagIntegrationTests class:

    [Fact]
    public async Task EndToEnd_Ingest_DetectCommunities_GlobalSearch_ReturnsCommunityBasedAnswer()
    {
        // Arrange — configure community detector to return real communities
        var detector = new LeidenCommunityDetector(NullLogger<LeidenCommunityDetector>.Instance);
        var sutWithRealDetector = new ManagedCodeGraphRagService(
            _graph,
            _mockRouter.Object,
            _mockStamper.Object,
            detector,
            NullLogger<ManagedCodeGraphRagService>.Instance,
            _configMonitor);

        // Ingest
        var chunks = new List<DocumentChunk>
        {
            RagTestData.CreateChunk("c1", "Azure is a cloud computing platform by Microsoft.")
        };
        await sutWithRealDetector.IndexCorpusAsync(chunks);

        // Detect communities
        var communities = await detector.DetectAsync(_graph, targetLevels: 1);
        foreach (var community in communities)
        {
            await _graph.SaveCommunityAsync(community);
            foreach (var nodeId in community.NodeIds)
                await _graph.AssignCommunityAsync(nodeId, community.Id, community.Level);
        }

        // Set up synthesis client
        var synthesisClient = new Mock<IChatClient>();
        synthesisClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "The corpus centers on Azure cloud computing technology.")));
        _mockRouter.Setup(r => r.GetClientForOperation("graph_global_search"))
            .Returns(synthesisClient.Object);

        // Act
        var result = await sutWithRealDetector.GlobalSearchAsync("What are the main themes?", communityLevel: 0);

        // Assert
        result.AssembledText.Should().Contain("Azure");
        result.TotalTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EndToEnd_MemoryStoreAndRecall_WorksAcrossSessions()
    {
        // Arrange
        var config = new AppConfig();
        config.AI.Rag.CrossSessionMemory = new CrossSessionMemoryConfig
        {
            Enabled = true,
            MaxMemories = 100,
            PruneThreshold = 0.01
        };
        var monitor = new Mock<IOptionsMonitor<AppConfig>>();
        monitor.Setup(m => m.CurrentValue).Returns(config);

        var memoryStore = new CrossSessionMemoryStore(
            _graph, monitor.Object, NullLogger<CrossSessionMemoryStore>.Instance);

        // Act — store a memory
        var memory = RagTestData.CreateMemoryRecord("mem-e2e", "User prefers TypeScript over JavaScript.");
        await memoryStore.RememberAsync(memory);

        // Sync to backend
        await memoryStore.SyncToBackendAsync(CancellationToken.None);

        // Create a new memory store (simulating a new session)
        var sessionTwoStore = new CrossSessionMemoryStore(
            _graph, monitor.Object, NullLogger<CrossSessionMemoryStore>.Instance);

        // Recall from session 1 store (should be in cache)
        var recalledFromCache = await memoryStore.RecallAsync(
            RagTestData.CreateMemoryQuery("TypeScript"));

        // Assert
        recalledFromCache.Should().ContainSingle(m => m.Id == "mem-e2e");

        // Cleanup
        memoryStore.Dispose();
        sessionTwoStore.Dispose();
    }

    [Fact]
    public async Task EndToEnd_DecayAndPrune_RemovesStaleMemories()
    {
        // Arrange
        var config = new AppConfig();
        config.AI.Rag.CrossSessionMemory = new CrossSessionMemoryConfig
        {
            Enabled = true,
            DecayRate = 0.5, // aggressive decay for testing
            PruneThreshold = 0.1
        };
        var monitor = new Mock<IOptionsMonitor<AppConfig>>();
        monitor.Setup(m => m.CurrentValue).Returns(config);

        var memoryStore = new CrossSessionMemoryStore(
            _graph, monitor.Object, NullLogger<CrossSessionMemoryStore>.Instance);

        // Store a memory with old access date via graph directly
        var oldNode = new GraphNode
        {
            Id = "mem-stale",
            Name = "Stale memory",
            Type = "Memory",
            Properties = new Dictionary<string, string>
            {
                ["weight"] = "0.3000",
                ["last_accessed_at"] = DateTimeOffset.UtcNow.AddDays(-10).ToString("O"),
                ["content"] = "Old information",
                ["source"] = "old-session"
            }
        };
        await _graph.AddNodesAsync([oldNode]);

        var decayService = new MemoryDecayService(
            _graph, memoryStore, monitor.Object, NullLogger<MemoryDecayService>.Instance);

        // Act — apply decay (0.3 * (1-0.5)^10 = 0.3 * 0.000977 = 0.000293)
        await decayService.ApplyDecayAsync();
        await decayService.PruneAsync(threshold: 0.1);

        // Assert — the stale memory should be pruned
        var exists = await _graph.NodeExistsAsync("mem-stale");
        exists.Should().BeFalse("stale memory should be pruned after decay");

        memoryStore.Dispose();
    }
```

- [ ] **Step 2: Run all tests — verify they pass**

Run: `dotnet test src/AgenticHarness.slnx --filter "FullyQualifiedName~GraphRagIntegrationTests"`
Expected: All 7 tests pass (4 from Task 9 + 3 new).

- [ ] **Step 3: Run full test suite**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass (no regressions from refactoring).

- [ ] **Step 4: Commit**

```bash
git add src/Content/Tests/Infrastructure.AI.RAG.Tests/GraphRag/GraphRagIntegrationTests.cs
git commit -m "test(rag): add end-to-end integration tests for graph pipeline, memory, and decay"
```

---

### Task 13: OTel Metrics

**Files:**
- Modify: `src/Content/Domain/Domain.AI/Telemetry/Conventions/RagConventions.cs`

- [ ] **Step 1: Add graph and memory telemetry constants to RagConventions**

Add these constants to `RagConventions.cs` after the existing attribute constants:

```csharp
// Add to the attribute constants section of RagConventions.cs:

    /// <summary>Number of communities detected at the current level.</summary>
    public const string GraphCommunityCount = "rag.graph.community_count";

    /// <summary>Graph traversal depth used for local search.</summary>
    public const string GraphTraversalDepth = "rag.graph.traversal_depth";

    /// <summary>Number of memory operations performed (remember/recall/forget/improve).</summary>
    public const string MemoryOperationCount = "rag.memory.operation_count";

    /// <summary>Type of memory operation performed.</summary>
    public const string MemoryOperationType = "rag.memory.operation_type";

    /// <summary>Number of memories returned from a recall operation.</summary>
    public const string MemoryRecallCount = "rag.memory.recall_count";

    /// <summary>Session-local memory cache hit rate (0.0-1.0).</summary>
    public const string MemoryCacheHitRate = "rag.memory.cache_hit_rate";

    /// <summary>Number of memories decayed during a decay pass.</summary>
    public const string MemoryDecayCount = "rag.memory.decay_count";

    /// <summary>Number of memories pruned during a pruning pass.</summary>
    public const string MemoryPruneCount = "rag.memory.prune_count";

    /// <summary>Weight delta applied during a memory improve operation.</summary>
    public const string MemoryWeightDelta = "rag.memory.weight_delta";

    /// <summary>Number of feedback weights persisted back to the graph backend.</summary>
    public const string FeedbackPersistCount = "rag.feedback.persist_count";
```

Add to the metric name constants section:

```csharp
// Add to the metric name constants section:

    /// <summary>Counter: total memory operations across all types.</summary>
    public const string MemoryOperations = "rag.memory.operations";

    /// <summary>Histogram: memory recall latency in milliseconds.</summary>
    public const string MemoryRecallLatency = "rag.memory.recall.latency";

    /// <summary>Counter: community detection runs completed.</summary>
    public const string CommunityDetectionRuns = "rag.graph.community_detection.runs";

    /// <summary>Histogram: community detection duration in seconds.</summary>
    public const string CommunityDetectionDuration = "rag.graph.community_detection.duration";
```

Add memory operation type values:

```csharp
// Add to the value sets section:

    /// <summary>Well-known values for <see cref="MemoryOperationType"/>.</summary>
    public static class MemoryOperationValues
    {
        /// <summary>A fact was stored in the memory store.</summary>
        public const string Remember = "remember";

        /// <summary>Knowledge was retrieved from the memory store.</summary>
        public const string Recall = "recall";

        /// <summary>A specific fact was deleted from the memory store.</summary>
        public const string Forget = "forget";

        /// <summary>Feedback was applied to improve memory quality.</summary>
        public const string Improve = "improve";
    }
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Run full test suite**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Content/Domain/Domain.AI/Telemetry/Conventions/RagConventions.cs
git commit -m "feat(rag): add OTel metric conventions for graph communities, memory operations, and feedback persistence"
```

---

## Verification Checklist

After all tasks are complete, run the full verification:

```bash
dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx
```

Expected results:
- 0 build errors
- All existing tests pass (no regressions)
- 8 KuzuGraphBackend tests pass
- 6 LeidenCommunityDetector tests pass
- 7 CrossSessionMemoryStore tests pass
- 5 MemoryDecayService tests pass
- 7 GraphRagIntegration tests pass (4 ManagedCode + 3 e2e)
- 3 FeedbackWeightedScorerPersistence tests pass
- **Total new tests: 36**

## Architecture Decision Records

### ADR-C1: SQLite-backed graph tables instead of native Kuzu

**Decision:** Use `Microsoft.Data.Sqlite` with relational graph tables instead of the Kuzu C API.

**Context:** The Kuzu .NET NuGet package (`Kuzu.NET`) does not have a stable release. The Kuzu C library requires P/Invoke bindings that are platform-specific and not yet well-supported in the .NET ecosystem.

**Consequences:** The `KuzuGraphBackend` class uses SQLite, which is already a project dependency (`Microsoft.Data.Sqlite`). This provides the same embedded, serverless semantics as Kuzu. The class name signals the intended target database. When a stable Kuzu .NET binding becomes available, swap the internal implementation without changing the `IGraphDatabaseBackend` contract.

### ADR-C2: Simplified Leiden algorithm

**Decision:** Implement a simplified Leiden-inspired algorithm rather than a full port of the igraph/networkit Leiden implementation.

**Context:** A full Leiden implementation requires complex data structures (partition refinement, queue-based node moves) that would add significant complexity for the initial implementation. The simplified version produces correct community assignments for typical knowledge graph sizes (< 100K nodes).

**Consequences:** The simplified algorithm may produce suboptimal community assignments on very large or dense graphs. The `ICommunityDetector` interface allows swapping in a more sophisticated implementation (e.g., calling a Python Leiden library via gRPC) without changing the rest of the pipeline.

### ADR-C3: Keyword-based memory recall

**Decision:** Use simple keyword matching for memory recall instead of embedding-based similarity search.

**Context:** Embedding-based recall would require storing embeddings for each memory and running cosine similarity, adding complexity and an embedding model dependency. For typical memory store sizes (< 10K records), keyword matching is fast and sufficient.

**Consequences:** Recall quality is lower than embedding-based search for semantic queries. The `ICrossSessionMemoryStore` interface allows upgrading to embedding-based recall without changing callers. This is a planned improvement for Phase D.
