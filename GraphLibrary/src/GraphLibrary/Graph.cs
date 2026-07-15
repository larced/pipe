namespace GraphLibrary;

/// <summary>
/// The library's core structure: a single directed, attributed multigraph that
/// unconditionally permits cycles, self-loops, and parallel edges (ADR 0001). Nodes and
/// edges carry caller-chosen compile-time payload types and are identified by opaque
/// <see cref="NodeHandle"/> / <see cref="EdgeHandle"/> values independent of payload (ADR 0003).
/// </summary>
/// <remarks>
/// <para>
/// This is the ticket-01 (#12) tracer slice: construct an empty graph, add nodes and edges
/// (including parallel edges and self-loops), and read them back. Removal, incidence indices,
/// handle guards, payload mutation, validators, and the fluent builder arrive in later tickets.
/// </para>
/// <para>
/// Backing is a generational-index slot-map (ADR 0002): parallel arrays for the node and edge
/// stores, with a generation counter per slot that will later ride in the handle for the
/// stale-handle guard. The public surface is deliberately independent of this container so a
/// future denser representation cannot change it.
/// </para>
/// </remarks>
public sealed class Graph<TNode, TEdge>
{
    // Distinct id per graph instance, stamped into every handle this graph mints so a later
    // ticket can reject a handle used against the wrong graph. Interlocked keeps ids unique
    // even if graphs are constructed concurrently (construction itself needs no other sync).
    private static int _nextGraphId;

    private readonly int _graphId = System.Threading.Interlocked.Increment(ref _nextGraphId);

    // Node slot-map: parallel arrays indexed by slot. Generation is reserved for the guard
    // and stale-handle logic of later tickets; in this slice every slot stays at generation 0.
    private TNode[] _nodePayloads = new TNode[InitialCapacity];
    private int[] _nodeGenerations = new int[InitialCapacity];
    private int _nodeCount;

    // Edge slot-map: source/target endpoints plus payload, single-sourcing edge identity (ADR 0002).
    private EdgeSlot[] _edges = new EdgeSlot[InitialCapacity];
    private TEdge[] _edgePayloads = new TEdge[InitialCapacity];
    private int _edgeCount;

    private const int InitialCapacity = 4;

    private struct EdgeSlot
    {
        public int Generation;
        public NodeHandle Source;
        public NodeHandle Target;
    }

    /// <summary>Adds a node carrying <paramref name="payload"/> and returns its stable handle.</summary>
    public NodeHandle AddNode(TNode payload)
    {
        int index = _nodeCount;
        if (index == _nodePayloads.Length)
        {
            Array.Resize(ref _nodePayloads, _nodePayloads.Length * 2);
            Array.Resize(ref _nodeGenerations, _nodeGenerations.Length * 2);
        }

        _nodePayloads[index] = payload;
        _nodeCount++;
        return new NodeHandle(index, _nodeGenerations[index], _graphId);
    }

    /// <summary>
    /// Adds a first-class edge from <paramref name="source"/> to <paramref name="target"/>
    /// carrying <paramref name="payload"/> and returns its stable handle. Parallel edges
    /// (repeat endpoint pairs) and self-loops (<paramref name="source"/> == <paramref name="target"/>)
    /// are accepted — no topology is refused by default.
    /// </summary>
    public EdgeHandle AddEdge(NodeHandle source, NodeHandle target, TEdge payload)
    {
        int index = _edgeCount;
        if (index == _edges.Length)
        {
            Array.Resize(ref _edges, _edges.Length * 2);
            Array.Resize(ref _edgePayloads, _edgePayloads.Length * 2);
        }

        _edges[index] = new EdgeSlot { Generation = 0, Source = source, Target = target };
        _edgePayloads[index] = payload;
        _edgeCount++;
        return new EdgeHandle(index, _edges[index].Generation, _graphId);
    }

    /// <summary>The handles of every node currently in the graph.</summary>
    public IEnumerable<NodeHandle> Nodes
    {
        get
        {
            for (int i = 0; i < _nodeCount; i++)
            {
                yield return new NodeHandle(i, _nodeGenerations[i], _graphId);
            }
        }
    }

    /// <summary>The handles of every edge currently in the graph.</summary>
    public IEnumerable<EdgeHandle> Edges
    {
        get
        {
            for (int i = 0; i < _edgeCount; i++)
            {
                yield return new EdgeHandle(i, _edges[i].Generation, _graphId);
            }
        }
    }

    /// <summary>Reads the payload of the node identified by <paramref name="handle"/>.</summary>
    public TNode GetNodePayload(NodeHandle handle) => _nodePayloads[handle.Index];

    /// <summary>Reads the payload of the edge identified by <paramref name="handle"/>.</summary>
    public TEdge GetEdgePayload(EdgeHandle handle) => _edgePayloads[handle.Index];

    /// <summary>The source node of the edge identified by <paramref name="handle"/>.</summary>
    public NodeHandle GetSource(EdgeHandle handle) => _edges[handle.Index].Source;

    /// <summary>The target node of the edge identified by <paramref name="handle"/>.</summary>
    public NodeHandle GetTarget(EdgeHandle handle) => _edges[handle.Index].Target;
}
