namespace GraphLibrary;

/// <summary>
/// The lean read surface over a <see cref="Graph{TNode,TEdge}"/> that both traversal and
/// rule-evaluation consume (CONTEXT.md → Readable graph): the node and edge sets, per-node
/// in/out <em>Incidence</em> and degree, and endpoint-pair access. Formalised here so the same
/// concrete object flows into traversal and rule-evaluation with no adapter (ticket #13).
/// </summary>
/// <remarks>
/// <para>
/// Every member is a <em>real</em> interface member (not a bolted-on extension), so incidence
/// and degree resolve by static dispatch when a consumer is generic over
/// <c>TGraph : IReadableGraph&lt;TNode,TEdge&gt;</c>. Layered traversal helpers (ADR 0004) build
/// <em>on</em> this surface rather than widening it.
/// </para>
/// <para>
/// The surface is <b>reads-pure</b>: no member performs lazy mutation or memoization. This is
/// load-bearing for the concurrent-read guarantee a later ticket relies on — many threads may
/// read a stable graph at once because reading never writes.
/// </para>
/// </remarks>
public interface IReadableGraph<TNode, TEdge>
{
    /// <summary>The handles of every node currently in the graph.</summary>
    IEnumerable<NodeHandle> Nodes { get; }

    /// <summary>The handles of every edge currently in the graph.</summary>
    IEnumerable<EdgeHandle> Edges { get; }

    /// <summary>Reads the payload of the node identified by <paramref name="handle"/>.</summary>
    TNode GetNodePayload(NodeHandle handle);

    /// <summary>Reads the payload of the edge identified by <paramref name="handle"/>.</summary>
    TEdge GetEdgePayload(EdgeHandle handle);

    /// <summary>The source node of the edge identified by <paramref name="handle"/>.</summary>
    NodeHandle GetSource(EdgeHandle handle);

    /// <summary>The target node of the edge identified by <paramref name="handle"/>.</summary>
    NodeHandle GetTarget(EdgeHandle handle);

    /// <summary>The out-edges of <paramref name="node"/> — its forward incidence.</summary>
    IEnumerable<EdgeHandle> GetOutEdges(NodeHandle node);

    /// <summary>
    /// The in-edges of <paramref name="node"/> — its reverse incidence. As cheap as
    /// <see cref="GetOutEdges"/>: both directions are indexed, not derived by scanning (ADR 0002).
    /// </summary>
    IEnumerable<EdgeHandle> GetInEdges(NodeHandle node);

    /// <summary>
    /// Every edge from <paramref name="source"/> to <paramref name="target"/>. Returns a
    /// collection, not a single edge: edges are first-class, so an endpoint pair may be joined by
    /// many parallel edges (CONTEXT.md → Edge). Empty when the pair is unconnected.
    /// </summary>
    IEnumerable<EdgeHandle> GetEdges(NodeHandle source, NodeHandle target);

    /// <summary>The number of out-edges of <paramref name="node"/> (its out-degree).</summary>
    int GetOutDegree(NodeHandle node);

    /// <summary>The number of in-edges of <paramref name="node"/> (its in-degree).</summary>
    int GetInDegree(NodeHandle node);
}
