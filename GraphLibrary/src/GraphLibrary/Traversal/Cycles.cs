namespace GraphLibrary.Traversal;

/// <summary>
/// The public <b>advisory</b> cycle queries over the layered <see cref="IReadableGraph{TNode,TEdge}"/>
/// read surface (ticket 12; spec story 20): read-only questions about acyclicity that need <em>no</em>
/// attached acyclicity <see cref="TopologyValidator"/>. They reuse ticket 07's internal cycle detection
/// (<c>Topology</c>) verbatim, so "reason about acyclicity" costs the same whether or not the graph is
/// being kept acyclic by a validator (CONTEXT.md → Validator).
/// </summary>
/// <remarks>
/// <para>
/// <b>Advisory, not enforcing.</b> A <see cref="TopologyValidator"/> refuses a mutation that would break a
/// topology; these queries merely <em>report</em> on the graph as it stands. A graph that never enabled the
/// acyclicity validator can still be asked whether it is acyclic, which edge-paths close a cycle, and
/// whether a proposed edge would introduce one — all without mutating anything.
/// </para>
/// <para>
/// <b>Results are eager snapshots</b> (spec story 39): each query runs to completion inside the call and
/// hands back a materialised, reusable result the caller may hold while the graph mutates. Because the walk
/// is synchronous there is no lazy cursor to invalidate mid-iteration; a stale or cross-graph endpoint on
/// <see cref="WouldCreateCycle{TNode,TEdge}"/> fails fast with <see cref="InvalidHandleException"/> on the call.
/// </para>
/// </remarks>
public static class Cycles
{
    /// <summary>
    /// Whether <paramref name="graph"/> currently contains no cycle at all — the advisory read of the
    /// acyclicity a <see cref="TopologyValidator.Acyclic"/> validator would enforce (spec story 20). A
    /// self-loop counts as a cycle, so a graph carrying one is not acyclic.
    /// </summary>
    public static bool IsAcyclic<TNode, TEdge>(this IReadableGraph<TNode, TEdge> graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return !Topology.ContainsCycle(graph);
    }

    /// <summary>
    /// A witness set of the cycles in <paramref name="graph"/>, each returned as a <see cref="Path"/> whose
    /// <see cref="Path.Start"/> equals its <see cref="Path.Target"/> — the closed edge-path that returns to
    /// the node it left (spec story 20). Empty exactly when <see cref="IsAcyclic{TNode,TEdge}"/> is true.
    /// This is a <em>diagnostic</em> set (at most one cycle per back edge the detection DFS meets), not the
    /// exponential enumeration of every elementary cycle; it is bounded and touches every cyclic region.
    /// Each cycle names the specific <see cref="EdgeHandle"/>s it follows, so it is unambiguous under
    /// parallel edges, and a self-loop appears as a length-one cycle.
    /// </summary>
    public static IReadOnlyList<Path> FindCycles<TNode, TEdge>(this IReadableGraph<TNode, TEdge> graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        IReadOnlyList<EdgeHandle[]> raw = Topology.FindCycles(graph);
        var cycles = new List<Path>(raw.Count);
        foreach (EdgeHandle[] edges in raw)
        {
            // A closed edge-path: the source of the first edge is also the target of the last, so Start and
            // Target coincide — a cycle expressed in the same edge-precise vocabulary as any other Path.
            NodeHandle at = graph.GetSource(edges[0]);
            cycles.Add(new Path(at, at, edges));
        }
        return cycles;
    }

    /// <summary>
    /// Would adding an edge <paramref name="source"/>→<paramref name="target"/> introduce a cycle, given
    /// <paramref name="graph"/> as it stands (spec story 20)? True iff the endpoints coincide (a self-loop is
    /// a trivial cycle) or <paramref name="target"/> already reaches <paramref name="source"/>, so the new
    /// edge would close the loop — the advisory read of the check <see cref="TopologyValidator.Acyclic"/>
    /// runs before accepting an edge. A stale or cross-graph endpoint fails fast with
    /// <see cref="InvalidHandleException"/> on the call.
    /// </summary>
    public static bool WouldCreateCycle<TNode, TEdge>(
        this IReadableGraph<TNode, TEdge> graph, NodeHandle source, NodeHandle target)
    {
        ArgumentNullException.ThrowIfNull(graph);
        // Eager endpoint guard, mirroring the reachability roster: reading each degree throws on a stale or
        // cross-graph handle here, on the call, rather than yielding a silently wrong answer.
        _ = graph.GetOutDegree(source);
        _ = graph.GetOutDegree(target);
        return Topology.WouldCreateCycle(graph, source, target);
    }
}
