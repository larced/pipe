namespace GraphLibrary.Traversal;

/// <summary>
/// Shortest-path queries over the layered <see cref="IReadableGraph{TNode,TEdge}"/> read surface
/// (ticket 11; ADR 0004): a <see cref="Path"/> between two Nodes returned as an edge-handle sequence
/// (spec story 33), computed by BFS for the unweighted default (spec story 34) or opt-in Dijkstra when a
/// non-negative edge weight is supplied (spec story 35). Every Path is unambiguous under parallel edges
/// because it records the specific <see cref="EdgeHandle"/>s it follows, not merely the nodes.
/// </summary>
/// <remarks>
/// <para>
/// <b>BFS is the default; weighting is opt-in.</b> Unweighted shortest paths need no configuration — the
/// two-argument overload counts hops and hands back a fewest-edges Path. Supplying a
/// <c>Func&lt;TEdge, double&gt;</c> weight switches to Dijkstra, which minimises total weight over
/// <b>non-negative</b> edges; a negative weight throws <see cref="ArgumentOutOfRangeException"/> the
/// moment it is encountered. Negative-weight / Bellman-Ford handling is explicitly out of scope for v1
/// (ADR 0004, YAGNI).
/// </para>
/// <para>
/// <b>Results are eager snapshots</b> (spec story 39): the Path is materialised inside the call and
/// returned as an immutable <see cref="Path"/> the caller may hold while the graph mutates. A stale or
/// cross-graph <paramref name="from"/>/<paramref name="to"/> fails fast with
/// <see cref="InvalidHandleException"/> on the call. When no Path exists the result is
/// <see langword="null"/>; a Path from a node to itself is the trivial zero-length Path (see
/// <see cref="Path"/>).
/// </para>
/// </remarks>
public static class Paths
{
    /// <summary>
    /// The unweighted shortest <see cref="Path"/> from <paramref name="from"/> to <paramref name="to"/>,
    /// found by BFS — the fewest-edges Path (spec story 34). Returns <see langword="null"/> when
    /// <paramref name="to"/> is not reachable from <paramref name="from"/>, and a zero-length Path when
    /// they are the same node.
    /// </summary>
    public static Path? ShortestPath<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph,
        NodeHandle from,
        NodeHandle to)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateEndpoints(graph, from, to);

        if (from == to)
        {
            return new Path(from, to, Array.Empty<EdgeHandle>());
        }

        // BFS: the first time a node is discovered is along a fewest-edges Path, so the predecessor edge
        // recorded then is optimal. Recording the specific EdgeHandle (not just the source node) is what
        // keeps the Path unambiguous under parallel edges (spec story 33).
        var cameBy = new Dictionary<NodeHandle, EdgeHandle>();
        var visited = new HashSet<NodeHandle> { from };
        var frontier = new Queue<NodeHandle>();
        frontier.Enqueue(from);

        while (frontier.Count > 0)
        {
            NodeHandle node = frontier.Dequeue();
            foreach (EdgeHandle edge in graph.GetOutEdges(node))
            {
                NodeHandle next = graph.GetTarget(edge);
                if (!visited.Add(next))
                {
                    continue; // already discovered by an equal-or-shorter Path
                }

                cameBy[next] = edge;
                if (next == to)
                {
                    return Reconstruct(graph, from, to, cameBy);
                }
                frontier.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>
    /// The weighted shortest <see cref="Path"/> from <paramref name="from"/> to <paramref name="to"/>,
    /// found by Dijkstra over the non-negative edge weights that <paramref name="weight"/> supplies — the
    /// least-total-weight Path (spec story 35). Returns <see langword="null"/> when <paramref name="to"/>
    /// is not reachable, and a zero-length Path when the endpoints coincide. Throws
    /// <see cref="ArgumentOutOfRangeException"/> if <paramref name="weight"/> yields a negative value for
    /// any edge it is asked to weigh (negative weights are out of scope; ADR 0004).
    /// </summary>
    public static Path? ShortestPath<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph,
        NodeHandle from,
        NodeHandle to,
        Func<TEdge, double> weight)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(weight);
        ValidateEndpoints(graph, from, to);

        if (from == to)
        {
            return new Path(from, to, Array.Empty<EdgeHandle>());
        }

        // Dijkstra: settle nodes in non-decreasing total distance. A node is final the first time it is
        // dequeued (non-negative weights guarantee no later, cheaper approach), so we can stop the instant
        // `to` settles. Stale queue entries — a node re-queued at a lower cost — are ignored on dequeue.
        var best = new Dictionary<NodeHandle, double> { [from] = 0d };
        var cameBy = new Dictionary<NodeHandle, EdgeHandle>();
        var settled = new HashSet<NodeHandle>();
        var frontier = new PriorityQueue<NodeHandle, double>();
        frontier.Enqueue(from, 0d);

        while (frontier.TryDequeue(out NodeHandle node, out double distance))
        {
            if (!settled.Add(node))
            {
                continue; // a stale entry for an already-settled node
            }
            if (node == to)
            {
                return Reconstruct(graph, from, to, cameBy);
            }

            foreach (EdgeHandle edge in graph.GetOutEdges(node))
            {
                NodeHandle next = graph.GetTarget(edge);
                if (settled.Contains(next))
                {
                    continue;
                }

                double step = weight(graph.GetEdgePayload(edge));
                if (step < 0d)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(weight), step,
                        "Dijkstra requires non-negative edge weights; negative weights are out of scope.");
                }

                double candidate = distance + step;
                if (!best.TryGetValue(next, out double known) || candidate < known)
                {
                    best[next] = candidate;
                    cameBy[next] = edge;
                    frontier.Enqueue(next, candidate);
                }
            }
        }

        return null;
    }

    // Follows the predecessor-edge sequence back from `to` to `from`, then reverses it into Path order.
    // Every node on a discovered Path has an entry, so this terminates at `from`.
    private static Path Reconstruct<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph,
        NodeHandle from,
        NodeHandle to,
        Dictionary<NodeHandle, EdgeHandle> cameBy)
    {
        var reversed = new List<EdgeHandle>();
        NodeHandle cursor = to;
        while (cursor != from)
        {
            EdgeHandle edge = cameBy[cursor];
            reversed.Add(edge);
            cursor = graph.GetSource(edge);
        }
        reversed.Reverse();
        return new Path(from, to, reversed.ToArray());
    }

    // Eager endpoint guard: reading each endpoint's degree throws InvalidHandleException on a stale or
    // cross-graph handle here, on the call, mirroring the reachability roster's per-seed guard — and
    // validates `to` even when the search never reaches it.
    private static void ValidateEndpoints<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph, NodeHandle from, NodeHandle to)
    {
        _ = graph.GetOutDegree(from);
        _ = graph.GetOutDegree(to);
    }
}
