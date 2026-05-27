using System.Diagnostics;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.AI.Telemetry.Conventions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG.GraphRag;

/// <summary>
/// Simplified Leiden-inspired community detection algorithm for knowledge graph partitioning.
/// Produces hierarchical communities at multiple resolution levels suitable for GraphRAG
/// global search, where each community gets a summarized representation of its member nodes.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses a two-phase approach at each level:
/// <list type="number">
///   <item><term>Initialization</term><description>
///     Nodes are seeded into communities via BFS connected-component discovery.
///   </description></item>
///   <item><term>Modularity Optimization</term><description>
///     Iteratively moves nodes to the neighbor community that yields the highest modularity
///     gain, for up to 50 iterations or until no improvement is found.
///   </description></item>
///   <item><term>Refinement</term><description>
///     Splits any community that became internally disconnected during optimization.
///   </description></item>
///   <item><term>Hierarchy</term><description>
///     For levels above 0, communities from the previous level become super-nodes and the
///     process repeats, producing a coarser partition.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Modularity gain formula: <c>(edgesToTarget / totalEdges) - (ki * communityDegree / (2 * totalEdges^2))</c>
/// where <c>ki</c> is the degree of the moving node and <c>communityDegree</c> is the
/// sum of degrees of all nodes in the target community.
/// </para>
/// </remarks>
public sealed partial class LeidenCommunityDetector : ICommunityDetector
{
    private static readonly ActivitySource _activitySource =
        new("Infrastructure.AI.RAG.GraphRag");

    private const int MaxIterations = 50;

    private readonly ILogger<LeidenCommunityDetector> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="LeidenCommunityDetector"/>.
    /// </summary>
    /// <param name="logger">Logger for recording community detection progress.</param>
    public LeidenCommunityDetector(ILogger<LeidenCommunityDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Community>> DetectAsync(
        IGraphDatabaseBackend graph,
        int targetLevels,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(graph);

        using var activity = _activitySource.StartActivity("LeidenCommunityDetector.DetectAsync");

        var allNodes = await graph.GetAllNodesAsync(cancellationToken).ConfigureAwait(false);
        if (allNodes.Count == 0)
        {
            _logger.LogDebug("LeidenCommunityDetector: empty graph, returning no communities");
            return [];
        }

        var allTriplets = await graph.GetTripletsAsync([], cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "LeidenCommunityDetector: {NodeCount} nodes, {EdgeCount} edges, {TargetLevels} levels",
            allNodes.Count, allTriplets.Count, targetLevels);

        var result = new List<Community>();

        // Working set for the current level: node IDs within each super-node
        // At level 0, each super-node is a single real node.
        var superNodes = allNodes
            .ToDictionary(n => n.Id, n => (IReadOnlyList<string>)[n.Id]);

        // Adjacency at the super-node level
        var adjacency = BuildAdjacency(allTriplets.Select(t => (t.Edge.SourceNodeId, t.Edge.TargetNodeId)));

        for (var level = 0; level < targetLevels; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nodeIds = superNodes.Keys.ToList();

            // Phase 1: seed communities via BFS connected components
            var assignment = SeedByCc(nodeIds, adjacency);

            // Phase 2: modularity optimization
            OptimizeModularity(nodeIds, adjacency, assignment);

            // Phase 3: connectivity refinement (split disconnected communities)
            RefineConnectivity(nodeIds, adjacency, assignment);

            // Build Community records for this level
            var levelCommunities = BuildCommunityRecords(level, assignment, superNodes);

            await PersistCommunitiesAsync(graph, levelCommunities, cancellationToken)
                .ConfigureAwait(false);

            result.AddRange(levelCommunities);

            _logger.LogDebug(
                "LeidenCommunityDetector: level {Level} produced {Count} communities",
                level, levelCommunities.Count);

            if (level + 1 >= targetLevels)
                break;

            // Build super-nodes for the next level:
            // each community becomes a super-node containing all real node IDs of its members.
            var nextSuperNodes = new Dictionary<string, IReadOnlyList<string>>();
            var nextAdjacency = new Dictionary<string, HashSet<string>>();

            foreach (var community in levelCommunities)
            {
                // The community's real-node members are the union of all current super-node members
                var realNodeIds = community.NodeIds
                    .SelectMany(snId => superNodes.TryGetValue(snId, out var members) ? members : [snId])
                    .Distinct()
                    .ToList();

                nextSuperNodes[community.Id] = realNodeIds;
                nextAdjacency[community.Id] = [];
            }

            // Wire adjacency between super-nodes based on cross-community edges in the current level
            var superNodeByCommunityMember = new Dictionary<string, string>();
            foreach (var (snId, _) in superNodes)
            {
                var comm = levelCommunities.FirstOrDefault(c => c.NodeIds.Contains(snId));
                if (comm is not null)
                    superNodeByCommunityMember[snId] = comm.Id;
            }

            foreach (var (src, targets) in adjacency)
            {
                if (!superNodeByCommunityMember.TryGetValue(src, out var srcComm))
                    continue;
                foreach (var tgt in targets)
                {
                    if (!superNodeByCommunityMember.TryGetValue(tgt, out var tgtComm))
                        continue;
                    if (srcComm == tgtComm)
                        continue;

                    if (!nextAdjacency.ContainsKey(srcComm))
                        nextAdjacency[srcComm] = [];
                    if (!nextAdjacency.ContainsKey(tgtComm))
                        nextAdjacency[tgtComm] = [];

                    nextAdjacency[srcComm].Add(tgtComm);
                    nextAdjacency[tgtComm].Add(srcComm);
                }
            }

            superNodes = nextSuperNodes;
            adjacency = nextAdjacency.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<string>)kvp.Value);
        }

        activity?.SetTag(RagConventions.GraphCommunityLevel, targetLevels);
        activity?.SetTag("rag.graph.community_count", result.Count);

        return result;
    }

    /// <summary>
    /// Converts the integer community assignments into <see cref="Community"/> records
    /// using the provided super-node map to expand members into real node IDs.
    /// </summary>
    private static List<Community> BuildCommunityRecords(
        int level,
        Dictionary<string, int> assignment,
        Dictionary<string, IReadOnlyList<string>> superNodes)
    {
        var grouped = assignment
            .GroupBy(kvp => kvp.Value, kvp => kvp.Key)
            .ToList();

        var communities = new List<Community>(grouped.Count);
        var index = 0;

        foreach (var group in grouped)
        {
            var superNodeIds = group.ToList();

            // Expand super-node IDs to real node IDs
            var realNodeIds = superNodeIds
                .SelectMany(snId => superNodes.TryGetValue(snId, out var members) ? members : [snId])
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            communities.Add(new Community
            {
                Id = $"community_{level}_{index}",
                Level = level,
                Summary = string.Empty, // LLM summarization is a separate pipeline step
                NodeIds = realNodeIds,
                Modularity = 0.0,       // Computed post-optimization; placeholder for now
            });

            index++;
        }

        return communities;
    }

    /// <summary>
    /// Persists all community records and their node assignments to the graph backend.
    /// </summary>
    private static async Task PersistCommunitiesAsync(
        IGraphDatabaseBackend graph,
        IReadOnlyList<Community> communities,
        CancellationToken cancellationToken)
    {
        foreach (var community in communities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await graph.SaveCommunityAsync(community, cancellationToken).ConfigureAwait(false);

            foreach (var nodeId in community.NodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await graph.AssignCommunityAsync(nodeId, community.Id, community.Level, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
