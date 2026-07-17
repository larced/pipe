namespace GraphLibrary.Traversal;

/// <summary>
/// Bounded simple-path enumeration over the layered <see cref="IReadableGraph{TNode,TEdge}"/> read surface
/// (ticket 12; ADR 0004): <see cref="AllSimplePaths{TNode,TEdge}"/> lists every <see cref="Path"/> from one
/// Node to another that repeats no Node, up to a caller-supplied length bound (spec story 36 depth-bounded).
/// Complementary to <see cref="Paths.ShortestPath{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,NodeHandle)"/>,
/// which returns a single fewest-edges route — this returns <em>all</em> the ways through, within the bound.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bound is mandatory, by design.</b> The number of simple paths between two Nodes grows
/// combinatorially, so there is no unbounded overload: the caller always states a maximum hop length, and
/// the enumeration never follows a route longer than that. A path may not revisit a Node (that is what
/// "simple" means), so cycles, self-loops, and parallel back-references never produce an infinite walk.
/// Because each Path names the specific <see cref="EdgeHandle"/>s it follows, parallel edges yield distinct
/// paths — two edges from <c>a</c> to <c>b</c> are two one-hop paths (spec story 33).
/// </para>
/// <para>
/// <b>Results are eager snapshots</b> (spec story 39): every Path is materialised inside the call and the
/// whole list is returned for the caller to hold while the graph mutates. Because the search is synchronous
/// there is no lazy cursor to invalidate mid-iteration; a stale or cross-graph <paramref name="from"/> /
/// <paramref name="to"/> fails fast with <see cref="InvalidHandleException"/> on the call. When the endpoints
/// coincide the sole simple path is the trivial zero-length <see cref="Path"/> (matching <see cref="Path"/>).
/// </para>
/// </remarks>
public static class SimplePaths
{
    /// <summary>
    /// Every simple <see cref="Path"/> from <paramref name="from"/> to <paramref name="to"/> of at most
    /// <paramref name="maxLength"/> edges — no Node repeated, listed in depth-first discovery order.
    /// A <paramref name="maxLength"/> of 0 admits only the trivial <c>from == to</c> path; a negative
    /// <paramref name="maxLength"/> throws <see cref="ArgumentOutOfRangeException"/>. Returns an empty list
    /// when no route within the bound exists.
    /// </summary>
    public static IReadOnlyList<Path> AllSimplePaths<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph,
        NodeHandle from,
        NodeHandle to,
        int maxLength)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (maxLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLength), maxLength, "Length bound must be zero or positive.");
        }

        // Eager endpoint guard: reading each degree throws InvalidHandleException on a stale or cross-graph
        // handle here, on the call, and validates `to` even when no route reaches it (as Paths does).
        _ = graph.GetOutDegree(from);
        _ = graph.GetOutDegree(to);

        var results = new List<Path>();

        // The trivial path is a simple path from a node to itself; record it up front. The walk below never
        // re-derives it because `from` is immediately on the path and cannot be revisited.
        if (from == to)
        {
            results.Add(new Path(from, to, Array.Empty<EdgeHandle>()));
        }

        // Iterative DFS with an explicit frame stack (never recursion — matching the other traversal walks):
        // `pathEdges` is the current route, `onPath` the nodes it already visits (so it stays simple), and
        // pathEdges.Count is the current depth, kept at or below maxLength.
        var onPath = new HashSet<NodeHandle> { from };
        var pathEdges = new List<EdgeHandle>();
        var stack = new Stack<IEnumerator<EdgeHandle>>();
        stack.Push(graph.GetOutEdges(from).GetEnumerator());

        while (stack.Count > 0)
        {
            IEnumerator<EdgeHandle> outEdges = stack.Peek();

            // At the length bound this frame can extend no further; fall through to backtrack.
            if (pathEdges.Count < maxLength && outEdges.MoveNext())
            {
                EdgeHandle edge = outEdges.Current;
                NodeHandle next = graph.GetTarget(edge);
                if (onPath.Contains(next))
                {
                    continue; // revisiting a node would break simplicity — skip this edge
                }

                pathEdges.Add(edge);
                if (next == to)
                {
                    // A route to the target: record it, then backtrack this edge to look for others. We do
                    // not descend past `to` — a simple path ends there.
                    results.Add(new Path(from, to, pathEdges.ToArray()));
                    pathEdges.RemoveAt(pathEdges.Count - 1);
                    continue;
                }

                onPath.Add(next);
                stack.Push(graph.GetOutEdges(next).GetEnumerator());
                continue;
            }

            // Frame exhausted (or bound reached): dispose it, drop the node it added from the active path,
            // and retract the edge that led into it. The root `from` carries no incoming edge.
            outEdges.Dispose();
            stack.Pop();
            if (pathEdges.Count > 0)
            {
                NodeHandle enteredNode = graph.GetTarget(pathEdges[^1]);
                onPath.Remove(enteredNode);
                pathEdges.RemoveAt(pathEdges.Count - 1);
            }
        }

        return results;
    }
}
