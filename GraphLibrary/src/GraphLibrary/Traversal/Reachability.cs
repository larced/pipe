namespace GraphLibrary.Traversal;

/// <summary>
/// The reachability roster (ticket 10): the common node-set questions in both directions, answered
/// cheaply over the lean <see cref="IReadableGraph{TNode,TEdge}"/> read surface (ADR 0004). Forward
/// <see cref="Reachable{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,System.Func{TNode,bool},System.Func{TEdge,bool},int?)"/>,
/// reverse <see cref="Ancestors{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,System.Func{TNode,bool},System.Func{TEdge,bool},int?)"/>,
/// the membership probe <see cref="IsReachable{TNode,TEdge}"/>, and the fan-in
/// <see cref="ReachableFromMany{TNode,TEdge}(IReadableGraph{TNode,TEdge},System.Collections.Generic.IEnumerable{NodeHandle},System.Func{TNode,bool},System.Func{TEdge,bool},int?)"/>
/// (CONTEXT.md → Reachability; spec stories 31–32, 36 depth-bounded, 37–38, 39, 86).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reverse is first-class.</b> <see cref="Ancestors{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,System.Func{TNode,bool},System.Func{TEdge,bool},int?)"/>
/// walks the in-Incidence exactly as the forward direction walks the out-Incidence; because both
/// directions are indexed (ADR 0002), "what depends on X" costs the same as "what X depends on" —
/// it is never re-derived by scanning (spec story 32).
/// </para>
/// <para>
/// <b>The start is excluded.</b> A reachable set is the nodes reached by following <em>at least one</em>
/// Edge (CONTEXT.md → Reachability), so the seed(s) are the query origin, not a member of the answer —
/// this is the "everything downstream of X" / "all transitive dependencies of X" set. A seed that lies
/// on a cycle reappears because a genuine edge path returns to it. <see cref="IsReachable{TNode,TEdge}"/>
/// follows the same rule: <c>IsReachable(x, x)</c> is <see langword="true"/> only when <c>x</c> sits on a cycle.
/// </para>
/// <para>
/// <b>Filtering is inline, no DSL</b> (spec story 37). A <c>nodeFilter</c> drops a discovered Node from
/// the result <em>and</em> prunes the walk through it (a route behind a rejected node is cut, but a node
/// still reachable by an allowed route survives); the seeds themselves are never subjected to it — the
/// caller chose them. An <c>edgeFilter</c> is consulted before an Edge is followed, in whichever
/// direction the walk runs. <c>maxDepth</c> bounds the walk by shortest hop-distance from the nearest
/// seed (depth 0 = the seeds, excluded; depth 1 = direct neighbours).
/// </para>
/// <para>
/// <b>Results are eager snapshots</b> (spec story 39): each query materialises a <see cref="HashSet{T}"/>
/// in one shot and hands back an <see cref="IReadOnlySet{T}"/> the caller can hold and reuse while the
/// graph mutates. Because the walk runs to completion inside the call, there is no lazy cursor to
/// invalidate — a stale or cross-graph seed fails fast with <see cref="InvalidHandleException"/> on the
/// call itself.
/// </para>
/// </remarks>
public static class Reachability
{
    /// <summary>
    /// The node set reachable from <paramref name="start"/> by following out-Edges — "everything
    /// downstream of X" (spec story 31). Excludes <paramref name="start"/> unless a cycle returns to it.
    /// Optionally constrained by inline <paramref name="nodeFilter"/> / <paramref name="edgeFilter"/>
    /// predicates and an optional <paramref name="maxDepth"/> hop bound (spec stories 36–37).
    /// </summary>
    public static IReadOnlySet<NodeHandle> Reachable<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph,
        NodeHandle start,
        Func<TNode, bool>? nodeFilter = null,
        Func<TEdge, bool>? edgeFilter = null,
        int? maxDepth = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Collect(graph, new[] { start }, reverse: false, nodeFilter, edgeFilter, maxDepth);
    }

    /// <summary>
    /// The node set that reaches <paramref name="node"/> by following in-Edges — "what depends on X",
    /// the reverse of <see cref="Reachable{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,System.Func{TNode,bool},System.Func{TEdge,bool},int?)"/>
    /// and as cheap as it (ADR 0002; spec stories 32, 86). Excludes <paramref name="node"/> unless a
    /// cycle returns to it. Same filtering and depth-bound semantics as the forward direction.
    /// </summary>
    public static IReadOnlySet<NodeHandle> Ancestors<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph,
        NodeHandle node,
        Func<TNode, bool>? nodeFilter = null,
        Func<TEdge, bool>? edgeFilter = null,
        int? maxDepth = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Collect(graph, new[] { node }, reverse: true, nodeFilter, edgeFilter, maxDepth);
    }

    /// <summary>
    /// Whether <paramref name="to"/> is reachable from <paramref name="from"/> along out-Edges, subject
    /// to the same optional filters and depth bound (spec story 86). Stops as soon as
    /// <paramref name="to"/> is discovered rather than materialising the whole downstream set. Following
    /// the reachable-set rule, <c>IsReachable(x, x)</c> is <see langword="true"/> only when <c>x</c> lies
    /// on a cycle.
    /// </summary>
    public static bool IsReachable<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph,
        NodeHandle from,
        NodeHandle to,
        Func<TNode, bool>? nodeFilter = null,
        Func<TEdge, bool>? edgeFilter = null,
        int? maxDepth = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Walk(graph, new[] { from }, reverse: false, nodeFilter, edgeFilter, maxDepth)
            .Any(discovered => discovered == to);
    }

    /// <summary>
    /// The union of the downstream node sets of every seed in <paramref name="starts"/> — fan-out from
    /// many origins at once (spec story 86). A seed reached from another seed is not added: the seeds
    /// are collectively the query origin, so the result is everything reached from them by following at
    /// least one Edge. Same filters and depth bound as the single-seed form.
    /// </summary>
    public static IReadOnlySet<NodeHandle> ReachableFromMany<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph,
        IEnumerable<NodeHandle> starts,
        Func<TNode, bool>? nodeFilter = null,
        Func<TEdge, bool>? edgeFilter = null,
        int? maxDepth = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(starts);
        return Collect(graph, starts, reverse: false, nodeFilter, edgeFilter, maxDepth);
    }

    /// <summary>
    /// <see cref="ReachableFromMany{TNode,TEdge}(IReadableGraph{TNode,TEdge},System.Collections.Generic.IEnumerable{NodeHandle},System.Func{TNode,bool},System.Func{TEdge,bool},int?)"/>
    /// seeded from an opt-in <see cref="SecondaryIndex{TKey}"/>: the handles whose payload yields
    /// <paramref name="key"/> become the seeds (spec story 38). The acceleration is content-based seed
    /// <em>selection</em> — the walk itself is unchanged.
    /// </summary>
    public static IReadOnlySet<NodeHandle> ReachableFromMany<TNode, TEdge, TKey>(
        this IReadableGraph<TNode, TEdge> graph,
        SecondaryIndex<TKey> index,
        TKey key,
        Func<TNode, bool>? nodeFilter = null,
        Func<TEdge, bool>? edgeFilter = null,
        int? maxDepth = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(index);
        return graph.ReachableFromMany(index.Lookup(key), nodeFilter, edgeFilter, maxDepth);
    }

    // Materialises the walk into an eager, reusable snapshot (spec story 39). Draining the iterator here
    // also forces the seed guard and any predicate to run inside the call, so a stale/cross-graph seed
    // fails fast on the call rather than on a deferred enumeration.
    private static HashSet<NodeHandle> Collect<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph,
        IEnumerable<NodeHandle> seeds,
        bool reverse,
        Func<TNode, bool>? nodeFilter,
        Func<TEdge, bool>? edgeFilter,
        int? maxDepth)
        => new(Walk(graph, seeds, reverse, nodeFilter, edgeFilter, maxDepth));

    // One layered BFS engine behind every roster member. Forward walks out-edges (target endpoints),
    // reverse walks in-edges (source endpoints) — symmetric because both incidences are indexed
    // (ADR 0002), so Ancestors costs what Reachable costs. Seeds sit at depth 0 and are never yielded;
    // each discovered node is yielded exactly once, in non-decreasing hop-distance from the nearest seed.
    private static IEnumerable<NodeHandle> Walk<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph,
        IEnumerable<NodeHandle> seeds,
        bool reverse,
        Func<TNode, bool>? nodeFilter,
        Func<TEdge, bool>? edgeFilter,
        int? maxDepth)
    {
        if (maxDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth), maxDepth, "Depth bound must be zero or positive.");
        }

        // Two guards, deliberately distinct. `yielded` is result membership: a node belongs to the
        // answer iff it is reached by following at least one edge — so a seed is not a member until an
        // edge path (e.g. a cycle, or another seed) discovers it. `expanded` bounds work: every node,
        // seed or not, has its neighbours walked at most once. Keeping them separate is what makes the
        // start excluded yet re-includable on a cycle, and makes ReachableFromMany the true union of
        // each seed's reachable set.
        var yielded = new HashSet<NodeHandle>();
        var expanded = new HashSet<NodeHandle>();
        var frontier = new Queue<NodeHandle>();
        foreach (NodeHandle seed in seeds)
        {
            // Eager per-seed guard: reading the seed's degree throws InvalidHandleException on a stale
            // or cross-graph handle here, on the call, mirroring Traverse's eager seed resolution.
            _ = reverse ? graph.GetInDegree(seed) : graph.GetOutDegree(seed);
            if (expanded.Add(seed))
            {
                frontier.Enqueue(seed); // enqueued to expand, but not yet a result member
            }
        }

        // Level-synchronous BFS: drain one depth at a time so maxDepth bounds by shortest hop-distance.
        for (int depth = 0; frontier.Count > 0 && (maxDepth is null || depth < maxDepth); depth++)
        {
            for (int remaining = frontier.Count; remaining > 0; remaining--)
            {
                NodeHandle node = frontier.Dequeue();
                foreach (NodeHandle next in Step(graph, node, reverse, edgeFilter))
                {
                    if (nodeFilter is not null && !nodeFilter(graph.GetNodePayload(next)))
                    {
                        continue; // rejected: excluded from the set and not walked through
                    }
                    if (yielded.Add(next))
                    {
                        yield return next; // first edge-discovery: now a result member
                    }
                    if (expanded.Add(next))
                    {
                        frontier.Enqueue(next); // first time seen at all: expand its neighbours once
                    }
                }
            }
        }
    }

    // The neighbours of one node in the walk's direction, honouring the edge filter. Forward yields
    // out-edge targets; reverse yields in-edge sources. Parallel edges may offer the same neighbour
    // twice — the caller's visited-set collapses that to a single discovery.
    private static IEnumerable<NodeHandle> Step<TNode, TEdge>(
        IReadableGraph<TNode, TEdge> graph,
        NodeHandle node,
        bool reverse,
        Func<TEdge, bool>? edgeFilter)
    {
        IEnumerable<EdgeHandle> edges = reverse ? graph.GetInEdges(node) : graph.GetOutEdges(node);
        foreach (EdgeHandle edge in edges)
        {
            if (edgeFilter is not null && !edgeFilter(graph.GetEdgePayload(edge)))
            {
                continue;
            }
            yield return reverse ? graph.GetSource(edge) : graph.GetTarget(edge);
        }
    }
}
