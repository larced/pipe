namespace GraphLibrary.Traversal;

/// <summary>
/// Dependency ordering over the layered <see cref="IReadableGraph{TNode,TEdge}"/> read surface
/// (ticket 12; ADR 0004): <see cref="TopologicalSort{TNode,TEdge}"/> lists every Node before the Nodes it
/// points at, so out-edges read as "must come after" (spec story 85). Defined only for an acyclic graph —
/// a cycle has no such order, and the sort says so rather than returning a wrong one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kahn's algorithm.</b> Repeatedly emit a Node whose remaining in-degree is zero, then relax its
/// out-edges. This is iterative (no recursion — the target scale is ~25k–50k nodes, matching the other
/// traversal walks) and doubles as its own cycle check: if some Node never reaches in-degree zero, the
/// graph has a cycle and no topological order exists. Parallel edges are counted per edge and relaxed per
/// edge, so they cancel out cleanly; a self-loop makes a Node depend on itself and is reported as a cycle.
/// </para>
/// <para>
/// <b>Results are eager snapshots</b> (spec story 39): the order is materialised inside the call and handed
/// back as an <see cref="IReadOnlyList{T}"/> the caller may hold while the graph mutates. Because the sort
/// is synchronous there is no lazy cursor to invalidate mid-iteration.
/// </para>
/// </remarks>
public static class Ordering
{
    /// <summary>
    /// Every Node of <paramref name="graph"/> in dependency order — each Node precedes every Node reachable
    /// from it by one out-edge (spec story 85). Ready Nodes are emitted in the graph's node-enumeration
    /// order, so the result is deterministic for a given graph. Throws <see cref="InvalidOperationException"/>
    /// if the graph contains a cycle (including a self-loop): no topological order exists.
    /// </summary>
    public static IReadOnlyList<NodeHandle> TopologicalSort<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Remaining in-degree per node — counted per in-edge so parallel edges and self-loops are honoured.
        var remaining = new Dictionary<NodeHandle, int>();
        int nodeCount = 0;
        foreach (NodeHandle node in graph.Nodes)
        {
            remaining[node] = graph.GetInDegree(node);
            nodeCount++;
        }

        // Seed with the roots (in-degree zero), in node-enumeration order, for a deterministic result.
        var ready = new Queue<NodeHandle>();
        foreach (NodeHandle node in graph.Nodes)
        {
            if (remaining[node] == 0)
            {
                ready.Enqueue(node);
            }
        }

        var order = new List<NodeHandle>(nodeCount);
        while (ready.Count > 0)
        {
            NodeHandle node = ready.Dequeue();
            order.Add(node);
            foreach (EdgeHandle edge in graph.GetOutEdges(node))
            {
                NodeHandle target = graph.GetTarget(edge);
                if (--remaining[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        // Every node emitted iff the graph is acyclic; a shortfall means some node never reached in-degree
        // zero — it sits on a cycle, so no topological order exists.
        if (order.Count != nodeCount)
        {
            throw new InvalidOperationException(
                "The graph contains a cycle, so it has no topological order. "
                + "Use Cycles.FindCycles to locate the offending edges, or attach the acyclicity validator.");
        }

        return order;
    }
}
