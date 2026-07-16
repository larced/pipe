namespace GraphLibrary.Traversal;

/// <summary>
/// The layered traversal/query API (ADR 0004): extension methods in the <c>GraphLibrary.Traversal</c>
/// namespace over the lean <see cref="IReadableGraph{TNode,TEdge}"/> read surface, physically separate
/// from both the mutable core and rule-evaluation. Importing this namespace is opt-in — a caller who
/// never does sees "just a graph" (spec story 41).
/// </summary>
/// <remarks>
/// <para>
/// This ticket (09) ships the pull-based primitive everything else composes on: a lazy, LINQ-composable
/// <see cref="Traverse{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,System.Func{NodeHandle,VisitControl},TraversalOrder)"/>
/// steered by a visitor, terminating through cycles / self-loops / parallel edges via a visited-set,
/// plus the Incidence primitives <see cref="OutEdges{TNode,TEdge}"/> / <see cref="InEdges{TNode,TEdge}"/>
/// and the endpoint-projecting <see cref="OutNeighbors{TNode,TEdge}"/> / <see cref="InNeighbors{TNode,TEdge}"/>.
/// Reachability, edge-handle paths, components and shortest-path
/// build on this surface in later tickets — never reaching into the store (ADR 0004).
/// </para>
/// <para>
/// <b>Liveness.</b> A lazy <see cref="Traverse{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,TraversalOrder)"/>
/// fails fast with <see cref="System.InvalidOperationException"/> if the graph is structurally mutated
/// mid-iteration (spec story 40), observing ticket 06's structural-version counter through the internal
/// <see cref="IStructurallyVersioned"/> seam. A read surface that carries no such counter simply skips
/// the check. Anything a caller materialises (e.g. <c>Traverse(...).ToList()</c>) is an eager, stable
/// snapshot they can hold and reuse safely (spec story 39).
/// </para>
/// </remarks>
public static class GraphTraversal
{
    /// <summary>
    /// The out-edges of <paramref name="node"/> — its forward Incidence, named for the
    /// <c>Traversal</c> surface. A thin projection of <see cref="IReadableGraph{TNode,TEdge}.GetOutEdges"/>.
    /// </summary>
    public static IEnumerable<EdgeHandle> OutEdges<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph, NodeHandle node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.GetOutEdges(node);
    }

    /// <summary>
    /// The in-edges of <paramref name="node"/> — its reverse Incidence, as cheap as
    /// <see cref="OutEdges{TNode,TEdge}"/> (ADR 0002). A thin projection of
    /// <see cref="IReadableGraph{TNode,TEdge}.GetInEdges"/>.
    /// </summary>
    public static IEnumerable<EdgeHandle> InEdges<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph, NodeHandle node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.GetInEdges(node);
    }

    /// <summary>
    /// The Nodes reached by following <paramref name="node"/>'s out-edges — the targets of its forward
    /// Incidence. A faithful projection: because edges are first-class, a neighbor reached by two
    /// parallel edges appears twice (one per edge); apply <see cref="Enumerable.Distinct{TSource}(IEnumerable{TSource})"/>
    /// if you want each neighbor once. A self-loop yields <paramref name="node"/> itself.
    /// </summary>
    public static IEnumerable<NodeHandle> OutNeighbors<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph, NodeHandle node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.GetOutEdges(node).Select(graph.GetTarget);
    }

    /// <summary>
    /// The Nodes that reach <paramref name="node"/> along their out-edges — the sources of its reverse
    /// Incidence, so "what points at X" is as cheap as "what does X point at" (ADR 0002). Parallel edges
    /// yield the same neighbor multiply, as with <see cref="OutNeighbors{TNode,TEdge}"/>.
    /// </summary>
    public static IEnumerable<NodeHandle> InNeighbors<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph, NodeHandle node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.GetInEdges(node).Select(graph.GetSource);
    }

    /// <summary>
    /// Lazily walks the graph forward from <paramref name="start"/> in <paramref name="order"/>,
    /// producing each reachable Node exactly once (spec story 28). The unsteered form — equivalent to a
    /// visitor that always returns <see cref="VisitControl.Continue"/>. LINQ-composable and pull-based, so
    /// a caller can stop early (<c>.Take(n)</c>, <c>.First(...)</c>) without materialising the whole walk.
    /// </summary>
    public static IEnumerable<NodeHandle> Traverse<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph, NodeHandle start,
        TraversalOrder order = TraversalOrder.DepthFirst)
        => graph.Traverse(start, static _ => VisitControl.Continue, order);

    /// <summary>
    /// Lazily walks the graph forward from <paramref name="start"/> in <paramref name="order"/>, steered
    /// by <paramref name="visitor"/>: for each Node reached, the visitor's <see cref="VisitControl"/>
    /// decides whether to descend (<see cref="VisitControl.Continue"/>), prune that branch
    /// (<see cref="VisitControl.SkipDescendants"/>), or halt the whole walk
    /// (<see cref="VisitControl.Stop"/>) — spec story 29. Every Node is still produced in the case that
    /// applies; the control governs only what follows.
    /// </summary>
    /// <remarks>
    /// A visited-set guarantees termination through cycles, self-loops, and parallel edges (spec story 30):
    /// a Node is recorded the first time it is discovered and never expanded twice. The walk is lazy and
    /// fails fast with <see cref="InvalidOperationException"/> if the graph is structurally mutated
    /// mid-iteration (spec story 40). A stale or cross-graph <paramref name="start"/> fails fast on the
    /// call (not on a deferred <c>MoveNext</c>) with <see cref="InvalidHandleException"/>.
    /// </remarks>
    public static IEnumerable<NodeHandle> Traverse<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph, NodeHandle start,
        Func<NodeHandle, VisitControl> visitor,
        TraversalOrder order = TraversalOrder.DepthFirst)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(visitor);

        // Resolve the seed eagerly so a stale/cross-graph start throws on the call rather than on a
        // deferred MoveNext — matching the incidence readers' eager guard (Graph.GetOutEdges).
        _ = graph.GetOutDegree(start);

        return order == TraversalOrder.BreadthFirst
            ? BreadthFirst(graph, start, visitor)
            : DepthFirst(graph, start, visitor);
    }

    // Pre-order DFS over an explicit stack (never recursion — the target scale is ~25k–50k nodes and a
    // deep recursion would risk a stack overflow, matching Topology's iterative walks). Out-neighbors
    // are pushed in reverse so siblings pop in incidence order.
    private static IEnumerable<NodeHandle> DepthFirst<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph, NodeHandle start, Func<NodeHandle, VisitControl> visitor)
    {
        var versioned = graph as IStructurallyVersioned;
        int version = versioned?.StructuralVersion ?? 0;

        var visited = new HashSet<NodeHandle> { start };
        var stack = new Stack<NodeHandle>();
        stack.Push(start);
        var children = new List<NodeHandle>();

        while (stack.Count > 0)
        {
            // Re-check before any graph read this step: a structural mutation during the caller's last
            // yield-body fails fast here with InvalidOperationException (spec story 40), before we touch
            // incidence — so mutation is reported uniformly, not as a stale-handle error.
            if (versioned is not null && version != versioned.StructuralVersion)
            {
                throw StructurallyModified();
            }

            NodeHandle node = stack.Pop();
            VisitControl control = visitor(node);

            // Expand before yielding: the caller only runs (and only mutates) during the yield, so all
            // graph reads for this step happen first, at a consistent version.
            if (control == VisitControl.Continue)
            {
                children.Clear();
                foreach (NodeHandle neighbor in OutNeighbors(graph, node))
                {
                    if (visited.Add(neighbor))
                    {
                        children.Add(neighbor);
                    }
                }
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }

            yield return node;

            if (control == VisitControl.Stop)
            {
                yield break;
            }
        }
    }

    // Breadth-first over a queue: same visited-set, same steering, same fail-fast; frontier is FIFO so
    // Nodes come out in non-decreasing distance from the start.
    private static IEnumerable<NodeHandle> BreadthFirst<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph, NodeHandle start, Func<NodeHandle, VisitControl> visitor)
    {
        var versioned = graph as IStructurallyVersioned;
        int version = versioned?.StructuralVersion ?? 0;

        var visited = new HashSet<NodeHandle> { start };
        var queue = new Queue<NodeHandle>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            if (versioned is not null && version != versioned.StructuralVersion)
            {
                throw StructurallyModified();
            }

            NodeHandle node = queue.Dequeue();
            VisitControl control = visitor(node);

            if (control == VisitControl.Continue)
            {
                foreach (NodeHandle neighbor in OutNeighbors(graph, node))
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            yield return node;

            if (control == VisitControl.Stop)
            {
                yield break;
            }
        }
    }

    // Mirrors the core's mid-iteration failure wording (spec story 80). Best-effort: raised on the
    // enumerating thread when it observes the counter has moved, not a cross-thread guarantee.
    private static InvalidOperationException StructurallyModified() =>
        new("The graph was structurally modified; traversal may not continue. "
            + "Reads are caller-synchronized — synchronize your writes against concurrent reads.");
}
