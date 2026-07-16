using System.Collections.ObjectModel;

namespace GraphLibrary.Traversal;

/// <summary>
/// A concrete Path between two Nodes, expressed as an ordered sequence of <see cref="EdgeHandle"/>s
/// (CONTEXT.md → Path; spec story 33). Because a Path names the specific edges it follows — not just the
/// nodes it passes through — it is <b>unambiguous under parallel edges</b>: when two nodes are joined by
/// several edges, a Path records exactly which one it used.
/// </summary>
/// <remarks>
/// <para>
/// A Path is an <b>eager, immutable snapshot</b> (spec story 39): its <see cref="Edges"/> are captured in
/// one shot when the Path is computed, so a caller may hold and reuse it while the graph mutates. The
/// edges run in order — <see cref="Start"/> is the source of the first edge, each edge's target is the
/// next edge's source, and the final edge's target is <see cref="Target"/>.
/// </para>
/// <para>
/// <b>The trivial Path.</b> A Path from a node to itself is the empty edge sequence: <see cref="Start"/>
/// equals <see cref="Target"/> and <see cref="Length"/> is 0. This is the natural shortest-path answer —
/// a node is at distance 0 from itself — and is why <see cref="Traversal.Paths.ShortestPath{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,NodeHandle)"/>
/// returns a zero-length Path for <c>from == to</c> rather than <see langword="null"/> (which means "no
/// Path exists"). It differs deliberately from Reachability's self-exclusion rule, which is about
/// following <em>at least one</em> edge; a Path need not.
/// </para>
/// </remarks>
public sealed class Path
{
    internal Path(NodeHandle start, NodeHandle target, EdgeHandle[] edges)
    {
        Start = start;
        Target = target;
        // Wrap once so the exposed sequence cannot be cast back to the backing array and mutated — the
        // snapshot the caller holds is genuinely immutable (spec story 39).
        Edges = new ReadOnlyCollection<EdgeHandle>(edges);
    }

    /// <summary>The Node the Path departs from — the source of the first edge (or, for a trivial Path,
    /// the node itself).</summary>
    public NodeHandle Start { get; }

    /// <summary>The Node the Path arrives at — the target of the last edge (or, for a trivial Path,
    /// equal to <see cref="Start"/>).</summary>
    public NodeHandle Target { get; }

    /// <summary>
    /// The edges the Path follows, in order (CONTEXT.md → Path). Empty for the trivial
    /// <see cref="Start"/><c> == </c><see cref="Target"/> Path. Each handle names one specific edge, so
    /// parallel edges never make the Path ambiguous.
    /// </summary>
    public IReadOnlyList<EdgeHandle> Edges { get; }

    /// <summary>The number of edges (hops) in the Path. 0 for a trivial Path.</summary>
    public int Length => Edges.Count;
}
