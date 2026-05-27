namespace Infrastructure.AI.RAG.GraphRag;

public sealed partial class LeidenCommunityDetector
{
    /// <summary>
    /// Builds an undirected adjacency map from directed edge pairs.
    /// Both directions are added so the graph is treated as undirected during community detection.
    /// </summary>
    private static Dictionary<string, IReadOnlySet<string>> BuildAdjacency(
        IEnumerable<(string Src, string Tgt)> edges)
    {
        var adj = new Dictionary<string, HashSet<string>>();

        foreach (var (src, tgt) in edges)
        {
            if (!adj.TryGetValue(src, out var srcSet))
                adj[src] = srcSet = [];
            if (!adj.TryGetValue(tgt, out var tgtSet))
                adj[tgt] = tgtSet = [];

            srcSet.Add(tgt);
            tgtSet.Add(src);
        }

        return adj.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlySet<string>)kvp.Value);
    }

    /// <summary>
    /// Seeds community assignments via BFS connected-component discovery.
    /// Each connected component starts as its own community.
    /// </summary>
    private static Dictionary<string, int> SeedByCc(
        IReadOnlyList<string> nodeIds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> adjacency)
    {
        var assignment = new Dictionary<string, int>();
        var communityId = 0;

        foreach (var nodeId in nodeIds)
        {
            if (assignment.ContainsKey(nodeId))
                continue;

            // BFS from this unvisited node
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);
            assignment[nodeId] = communityId;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out var neighbors))
                    continue;

                foreach (var neighbor in neighbors)
                {
                    if (!assignment.ContainsKey(neighbor) && nodeIds.Contains(neighbor))
                    {
                        assignment[neighbor] = communityId;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            communityId++;
        }

        // Ensure all nodes have an assignment (isolated nodes not in adjacency)
        foreach (var nodeId in nodeIds)
        {
            if (!assignment.ContainsKey(nodeId))
                assignment[nodeId] = communityId++;
        }

        return assignment;
    }
}
