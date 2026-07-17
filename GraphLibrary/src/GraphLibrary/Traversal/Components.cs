namespace GraphLibrary.Traversal;

/// <summary>
/// Connected-component partitioning over the layered <see cref="IReadableGraph{TNode,TEdge}"/> read surface
/// (ticket 12; ADR 0004): <see cref="WeaklyConnectedComponents{TNode,TEdge}"/> groups the Nodes into the
/// maximal sets that hang together when edge direction is ignored (spec story 87) — the "which islands does
/// this graph fall into?" question consumers expect out of the box.
/// </summary>
/// <remarks>
/// <para>
/// <b>Weak connectivity treats every Edge as undirected.</b> Two Nodes share a component when some path of
/// edges — followed in either direction — joins them, so this walks each Node's in- <em>and</em> out-Incidence
/// (both cheap; ADR 0002). Every Node lands in exactly one component; an isolated Node (no incident edges) is
/// its own singleton component. Parallel edges and self-loops change no grouping.
/// </para>
/// <para>
/// <b>Results are eager snapshots</b> (spec story 39): the partition is materialised inside the call and
/// handed back as an <see cref="IReadOnlyList{T}"/> of reusable <see cref="IReadOnlySet{T}"/> components the
/// caller may hold while the graph mutates. Because the sweep is synchronous there is no lazy cursor to
/// invalidate mid-iteration. Components are returned in the order their first Node appears in the graph's
/// node enumeration, so the result is deterministic for a given graph.
/// </para>
/// </remarks>
public static class Components
{
    /// <summary>
    /// The weakly-connected components of <paramref name="graph"/>: the Nodes partitioned into maximal sets
    /// connected when edges are read as undirected (spec story 87). Each Node appears in exactly one set; an
    /// isolated Node forms a singleton. The union of the returned sets is every Node in the graph.
    /// </summary>
    public static IReadOnlyList<IReadOnlySet<NodeHandle>> WeaklyConnectedComponents<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var assigned = new HashSet<NodeHandle>();
        var components = new List<IReadOnlySet<NodeHandle>>();
        var frontier = new Queue<NodeHandle>();

        // Seed from every node in enumeration order so the partition is deterministic and every node —
        // including an edgeless island — is covered.
        foreach (NodeHandle seed in graph.Nodes)
        {
            if (!assigned.Add(seed))
            {
                continue; // already folded into an earlier component
            }

            // Undirected BFS from the seed: a node's neighbours are the endpoints of its in- and out-edges.
            var component = new HashSet<NodeHandle> { seed };
            frontier.Enqueue(seed);
            while (frontier.Count > 0)
            {
                NodeHandle node = frontier.Dequeue();
                foreach (NodeHandle neighbor in UndirectedNeighbors(graph, node))
                {
                    if (assigned.Add(neighbor))
                    {
                        component.Add(neighbor);
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    // Every node adjacent to `node` when edges are read as undirected: out-edge targets and in-edge sources.
    // A self-loop yields `node` (already assigned, so it is dropped); parallel edges yield a neighbour
    // repeatedly (collapsed by the assigned-set).
    private static IEnumerable<NodeHandle> UndirectedNeighbors<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph, NodeHandle node)
    {
        foreach (EdgeHandle edge in graph.GetOutEdges(node))
        {
            yield return graph.GetTarget(edge);
        }
        foreach (EdgeHandle edge in graph.GetInEdges(node))
        {
            yield return graph.GetSource(edge);
        }
    }
}
