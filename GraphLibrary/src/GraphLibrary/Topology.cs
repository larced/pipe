namespace GraphLibrary;

/// <summary>
/// The internal cycle-detection utility over the lean <see cref="IReadableGraph{TNode,TEdge}"/>
/// read surface (ADR 0001): it answers "does this graph contain a cycle?" and "would adding
/// <c>source→target</c> create one?" without mutating anything. Consumed by the acyclicity
/// <see cref="TopologyValidator"/> here, and surfaced as public advisory queries in ticket 12
/// (spec story 20) — the same primitive, exposed once the advisory namespace lands.
/// </summary>
/// <remarks>
/// Both walks are iterative with an explicit stack rather than recursive: the target scale is
/// ~25k–50k nodes and a deep recursion would otherwise risk a stack overflow. They read only through
/// <see cref="IReadableGraph{TNode,TEdge}"/> — incidence and endpoints — so they stay independent
/// of the slot-map backing (ADR 0002) and reusable by any read surface.
/// </remarks>
internal static class Topology
{
    // Three-colour DFS state. Unseen must be the default so a Dictionary miss reads as "not yet
    // visited": a grey (InProgress) node re-encountered along the current path is a back edge, i.e.
    // a cycle (a self-loop reaches its own grey node and so counts).
    private enum NodeState
    {
        Unseen = 0,
        InProgress,
        Done,
    }

    /// <summary>Does <paramref name="graph"/> currently contain a cycle (any self-loop included)?</summary>
    public static bool ContainsCycle<TNode, TEdge>(IReadableGraph<TNode, TEdge> graph)
    {
        var state = new Dictionary<NodeHandle, NodeState>();
        var stack = new Stack<(NodeHandle Node, IEnumerator<EdgeHandle> OutEdges)>();

        foreach (NodeHandle start in graph.Nodes)
        {
            if (state.GetValueOrDefault(start) != NodeState.Unseen)
            {
                continue;
            }

            state[start] = NodeState.InProgress;
            stack.Push((start, graph.GetOutEdges(start).GetEnumerator()));

            while (stack.Count > 0)
            {
                (NodeHandle node, IEnumerator<EdgeHandle> outEdges) = stack.Peek();
                if (outEdges.MoveNext())
                {
                    NodeHandle next = graph.GetTarget(outEdges.Current);
                    switch (state.GetValueOrDefault(next))
                    {
                        case NodeState.InProgress:
                            // Back edge to a node still on the active path — a cycle.
                            DisposeAll(stack);
                            return true;
                        case NodeState.Unseen:
                            state[next] = NodeState.InProgress;
                            stack.Push((next, graph.GetOutEdges(next).GetEnumerator()));
                            break;
                        // Done: already fully explored, no cycle through it — skip.
                    }
                }
                else
                {
                    state[node] = NodeState.Done;
                    outEdges.Dispose();
                    stack.Pop();
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Would adding an edge <paramref name="source"/>→<paramref name="target"/> introduce a cycle,
    /// given <paramref name="graph"/> as it is now? True iff the endpoints coincide (a self-loop is a
    /// trivial cycle) or <paramref name="target"/> already reaches <paramref name="source"/>, so the
    /// new edge would close the loop. Assumes the graph is otherwise acyclic — the invariant the
    /// acyclicity validator maintains — so a single reachability walk suffices.
    /// </summary>
    public static bool WouldCreateCycle<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph, NodeHandle source, NodeHandle target)
    {
        if (source == target)
        {
            return true;
        }

        var seen = new HashSet<NodeHandle> { target };
        var stack = new Stack<NodeHandle>();
        stack.Push(target);

        while (stack.Count > 0)
        {
            NodeHandle node = stack.Pop();
            foreach (EdgeHandle edge in graph.GetOutEdges(node))
            {
                NodeHandle next = graph.GetTarget(edge);
                if (next == source)
                {
                    return true;
                }
                if (seen.Add(next))
                {
                    stack.Push(next);
                }
            }
        }

        return false;
    }

    private static void DisposeAll(Stack<(NodeHandle Node, IEnumerator<EdgeHandle> OutEdges)> stack)
    {
        foreach ((_, IEnumerator<EdgeHandle> outEdges) in stack)
        {
            outEdges.Dispose();
        }
    }
}
