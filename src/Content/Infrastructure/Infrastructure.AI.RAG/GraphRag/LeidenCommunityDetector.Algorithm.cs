namespace Infrastructure.AI.RAG.GraphRag;

public sealed partial class LeidenCommunityDetector
{
    /// <summary>
    /// Iteratively moves nodes to the neighbor community that yields the highest positive
    /// modularity gain. Runs for at most <see cref="MaxIterations"/> passes or until stable.
    /// </summary>
    private static void OptimizeModularity(
        IReadOnlyList<string> nodeIds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> adjacency,
        Dictionary<string, int> assignment)
    {
        var totalEdges = adjacency.Values.Sum(s => s.Count) / 2.0;
        if (totalEdges <= 0)
            return;

        for (var iter = 0; iter < MaxIterations; iter++)
        {
            var improved = false;

            foreach (var nodeId in nodeIds)
            {
                if (!adjacency.TryGetValue(nodeId, out var neighbors) || neighbors.Count == 0)
                    continue;

                var currentComm = assignment[nodeId];

                // Degree of this node
                var ki = (double)neighbors.Count;

                // Count edges from this node to each neighboring community
                var edgesToCommunity = new Dictionary<int, double>();
                foreach (var neighbor in neighbors)
                {
                    if (!assignment.TryGetValue(neighbor, out var neighborComm))
                        continue;
                    if (!edgesToCommunity.ContainsKey(neighborComm))
                        edgesToCommunity[neighborComm] = 0;
                    edgesToCommunity[neighborComm]++;
                }

                // Community degree sums
                var communityDegree = new Dictionary<int, double>();
                foreach (var nId in nodeIds)
                {
                    var comm = assignment[nId];
                    var degree = adjacency.TryGetValue(nId, out var nNeighbors)
                        ? (double)nNeighbors.Count : 0;
                    if (!communityDegree.ContainsKey(comm))
                        communityDegree[comm] = 0;
                    communityDegree[comm] += degree;
                }

                // Find best community among neighbors (excluding current)
                var bestComm = currentComm;
                var bestGain = 0.0;

                foreach (var (candidateComm, edgesToTarget) in edgesToCommunity)
                {
                    if (candidateComm == currentComm)
                        continue;

                    var targetDegree = communityDegree.TryGetValue(candidateComm, out var d) ? d : 0;
                    var gain = (edgesToTarget / totalEdges)
                        - (ki * targetDegree / (2.0 * totalEdges * totalEdges));

                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestComm = candidateComm;
                    }
                }

                if (bestComm != currentComm)
                {
                    assignment[nodeId] = bestComm;
                    improved = true;
                }
            }

            if (!improved)
                break;
        }
    }

    /// <summary>
    /// Splits any community that became internally disconnected during optimization.
    /// Uses BFS within each community's induced subgraph to find connected components.
    /// </summary>
    private static void RefineConnectivity(
        IReadOnlyList<string> nodeIds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> adjacency,
        Dictionary<string, int> assignment)
    {
        var communityGroups = nodeIds
            .GroupBy(n => assignment[n])
            .ToDictionary(g => g.Key, g => g.ToHashSet());

        var nextCommId = assignment.Values.DefaultIfEmpty(0).Max() + 1;

        foreach (var (commId, members) in communityGroups)
        {
            if (members.Count <= 1)
                continue;

            // BFS within this community's induced subgraph
            var visited = new HashSet<string>();

            foreach (var seed in members)
            {
                if (visited.Contains(seed))
                    continue;

                var queue = new Queue<string>();
                queue.Enqueue(seed);
                visited.Add(seed);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!adjacency.TryGetValue(current, out var neighbors))
                        continue;

                    foreach (var neighbor in neighbors)
                    {
                        if (!visited.Contains(neighbor) && members.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                // Any members not yet visited form a new component — assign new community ID
                // (first component keeps original commId, subsequent ones get new IDs)
                if (visited.Count < members.Count)
                {
                    foreach (var member in members)
                    {
                        if (!visited.Contains(member))
                            assignment[member] = nextCommId;
                    }

                    nextCommId++;
                    break; // re-run on next iteration if needed; one split per pass is enough
                }
            }
        }
    }
}
