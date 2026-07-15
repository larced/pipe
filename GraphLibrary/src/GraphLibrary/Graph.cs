namespace GraphLibrary;

/// <summary>
/// The library's core structure: a single directed, attributed multigraph that
/// unconditionally permits cycles, self-loops, and parallel edges (ADR 0001). Nodes and
/// edges carry caller-chosen compile-time payload types and are identified by opaque
/// <see cref="NodeHandle"/> / <see cref="EdgeHandle"/> values independent of payload (ADR 0003).
/// Implements <see cref="IReadableGraph{TNode,TEdge}"/> directly, so the same concrete object
/// is the read surface traversal and rule-evaluation consume — no adapter.
/// </summary>
/// <remarks>
/// <para>
/// This slice (#13, on top of #12's add &amp; read) makes the graph mutable-consistent:
/// <see cref="RemoveNode"/> sweeps a node and every incident edge, <see cref="RemoveEdge"/>
/// retracts one edge and leaves its endpoints, and per-node in/out incidence indices make both
/// traversal directions and degree cheap. Handle guards, payload mutation, validators, and the
/// fluent builder arrive in later tickets.
/// </para>
/// <para>
/// Backing is a generational-index slot-map (ADR 0002): a node store carrying payload plus its
/// in/out incidence lists into the edge store, and a central edge store carrying endpoints plus
/// payload (single-sourcing edge identity). Removal tombstones a slot, bumps its generation, and
/// returns the index to a free-list for reuse; the bumped generation is what lets a stale handle
/// be told apart from a live one that happens to reuse the slot. The public surface is
/// deliberately independent of this container so a future denser representation cannot change it.
/// </para>
/// <para>
/// Reads are pure — no member below performs lazy mutation or memoization — which is load-bearing
/// for the concurrent-read guarantee a later ticket relies on.
/// </para>
/// </remarks>
public sealed class Graph<TNode, TEdge> : IReadableGraph<TNode, TEdge>
{
    // Distinct id per graph instance, stamped into every handle this graph mints so a later
    // ticket can reject a handle used against the wrong graph. Interlocked keeps ids unique
    // even if graphs are constructed concurrently (construction itself needs no other sync).
    private static int _nextGraphId;

    private readonly int _graphId = System.Threading.Interlocked.Increment(ref _nextGraphId);

    // Node slot-map. `_nodeHighWater` is the count of slots ever allocated; live slots are marked
    // Alive, freed slots are held in `_freeNodeSlots` for reuse. Generation rides in the handle.
    private NodeSlot[] _nodes = new NodeSlot[InitialCapacity];
    private int _nodeHighWater;
    private readonly Stack<int> _freeNodeSlots = new();

    // Edge slot-map: endpoints plus payload, single-sourcing edge identity (ADR 0002).
    private EdgeSlot[] _edges = new EdgeSlot[InitialCapacity];
    private int _edgeHighWater;
    private readonly Stack<int> _freeEdgeSlots = new();

    private const int InitialCapacity = 4;

    private struct NodeSlot
    {
        public TNode Payload;
        public int Generation;
        public bool Alive;
        // Indices into the edge store. Both directions are indexed so reverse reads are as cheap
        // as forward ones (ADR 0002); lists are allocated once per physical slot and reused.
        public List<int> OutEdges;
        public List<int> InEdges;
    }

    private struct EdgeSlot
    {
        public TEdge Payload;
        public int Generation;
        public bool Alive;
        public NodeHandle Source;
        public NodeHandle Target;
    }

    /// <summary>Adds a node carrying <paramref name="payload"/> and returns its stable handle.</summary>
    public NodeHandle AddNode(TNode payload)
    {
        int index;
        if (_freeNodeSlots.Count > 0)
        {
            index = _freeNodeSlots.Pop();
        }
        else
        {
            index = _nodeHighWater++;
            if (index == _nodes.Length)
            {
                Array.Resize(ref _nodes, _nodes.Length * 2);
            }
            _nodes[index].OutEdges = new List<int>();
            _nodes[index].InEdges = new List<int>();
        }

        ref NodeSlot slot = ref _nodes[index];
        slot.Payload = payload;
        slot.Alive = true;
        slot.OutEdges.Clear();
        slot.InEdges.Clear();
        return new NodeHandle(index, slot.Generation, _graphId);
    }

    /// <summary>
    /// Adds a first-class edge from <paramref name="source"/> to <paramref name="target"/>
    /// carrying <paramref name="payload"/> and returns its stable handle. Parallel edges
    /// (repeat endpoint pairs) and self-loops (<paramref name="source"/> == <paramref name="target"/>)
    /// are accepted — no topology is refused by default.
    /// </summary>
    public EdgeHandle AddEdge(NodeHandle source, NodeHandle target, TEdge payload)
    {
        int index;
        if (_freeEdgeSlots.Count > 0)
        {
            index = _freeEdgeSlots.Pop();
        }
        else
        {
            index = _edgeHighWater++;
            if (index == _edges.Length)
            {
                Array.Resize(ref _edges, _edges.Length * 2);
            }
        }

        ref EdgeSlot slot = ref _edges[index];
        slot.Payload = payload;
        slot.Alive = true;
        slot.Source = source;
        slot.Target = target;

        // Register incidence on both endpoints; a self-loop lands in both lists of the one node.
        _nodes[source.Index].OutEdges.Add(index);
        _nodes[target.Index].InEdges.Add(index);

        return new EdgeHandle(index, slot.Generation, _graphId);
    }

    /// <summary>
    /// Removes the node identified by <paramref name="handle"/> and every edge incident to it
    /// (in and out), with no manual bookkeeping required. Returns <see langword="true"/> if the
    /// node existed, <see langword="false"/> if it was already gone (idempotent).
    /// </summary>
    public bool RemoveNode(NodeHandle handle)
    {
        if (!TryResolve(handle, out int index))
        {
            return false;
        }

        ref NodeSlot slot = ref _nodes[index];

        // Snapshot the incident edges before retracting any — retraction mutates these lists, and
        // a self-loop appears in both, so dedupe to retract it exactly once.
        foreach (int edgeIndex in slot.OutEdges.Concat(slot.InEdges).Distinct().ToArray())
        {
            RetractEdge(edgeIndex);
        }

        slot.Alive = false;
        slot.Payload = default!;
        slot.Generation++;
        slot.OutEdges.Clear();
        slot.InEdges.Clear();
        _freeNodeSlots.Push(index);
        return true;
    }

    /// <summary>
    /// Retracts the single edge identified by <paramref name="handle"/> without touching its
    /// endpoints. Returns <see langword="true"/> if the edge existed, <see langword="false"/> if
    /// it was already gone — including when a prior <see cref="RemoveNode"/> swept it (idempotent).
    /// </summary>
    public bool RemoveEdge(EdgeHandle handle)
    {
        if (!TryResolve(handle, out int index))
        {
            return false;
        }

        RetractEdge(index);
        return true;
    }

    // Retracts a known-live edge slot: detach it from both endpoints' incidence, tombstone it,
    // bump its generation, and return the slot for reuse. Endpoints themselves are left intact.
    private void RetractEdge(int index)
    {
        ref EdgeSlot slot = ref _edges[index];
        _nodes[slot.Source.Index].OutEdges.Remove(index);
        _nodes[slot.Target.Index].InEdges.Remove(index);

        slot.Alive = false;
        slot.Payload = default!;
        slot.Generation++;
        _freeEdgeSlots.Push(index);
    }

    /// <inheritdoc/>
    public IEnumerable<NodeHandle> Nodes
    {
        get
        {
            for (int i = 0; i < _nodeHighWater; i++)
            {
                if (_nodes[i].Alive)
                {
                    yield return new NodeHandle(i, _nodes[i].Generation, _graphId);
                }
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<EdgeHandle> Edges
    {
        get
        {
            for (int i = 0; i < _edgeHighWater; i++)
            {
                if (_edges[i].Alive)
                {
                    yield return new EdgeHandle(i, _edges[i].Generation, _graphId);
                }
            }
        }
    }

    /// <inheritdoc/>
    public TNode GetNodePayload(NodeHandle handle) => _nodes[handle.Index].Payload;

    /// <inheritdoc/>
    public TEdge GetEdgePayload(EdgeHandle handle) => _edges[handle.Index].Payload;

    /// <inheritdoc/>
    public NodeHandle GetSource(EdgeHandle handle) => _edges[handle.Index].Source;

    /// <inheritdoc/>
    public NodeHandle GetTarget(EdgeHandle handle) => _edges[handle.Index].Target;

    /// <inheritdoc/>
    public IEnumerable<EdgeHandle> GetOutEdges(NodeHandle node) => Incidence(node, outgoing: true);

    /// <inheritdoc/>
    public IEnumerable<EdgeHandle> GetInEdges(NodeHandle node) => Incidence(node, outgoing: false);

    private IEnumerable<EdgeHandle> Incidence(NodeHandle node, bool outgoing)
    {
        if (!TryResolve(node, out int index))
        {
            yield break;
        }

        List<int> edges = outgoing ? _nodes[index].OutEdges : _nodes[index].InEdges;
        foreach (int edgeIndex in edges)
        {
            yield return new EdgeHandle(edgeIndex, _edges[edgeIndex].Generation, _graphId);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<EdgeHandle> GetEdges(NodeHandle source, NodeHandle target)
    {
        // Derived by scanning the source's out-incidence and filtering on target — no dedicated
        // (source, target) index (ADR 0002); cheap when sparse.
        if (!TryResolve(source, out int index))
        {
            yield break;
        }

        foreach (int edgeIndex in _nodes[index].OutEdges)
        {
            if (_edges[edgeIndex].Target == target)
            {
                yield return new EdgeHandle(edgeIndex, _edges[edgeIndex].Generation, _graphId);
            }
        }
    }

    /// <inheritdoc/>
    public int GetOutDegree(NodeHandle node) => TryResolve(node, out int index) ? _nodes[index].OutEdges.Count : 0;

    /// <inheritdoc/>
    public int GetInDegree(NodeHandle node) => TryResolve(node, out int index) ? _nodes[index].InEdges.Count : 0;

    // Resolves a handle to a live slot index, or reports it dead. This is the liveness test that
    // makes removals idempotent and skips reads over freed slots; the throwing cross-graph /
    // stale-handle guard (InvalidHandleException) is a later ticket (ADR 0003).
    private bool TryResolve(NodeHandle handle, out int index)
    {
        index = handle.Index;
        return handle.GraphId == _graphId
            && (uint)index < (uint)_nodeHighWater
            && _nodes[index].Alive
            && _nodes[index].Generation == handle.Generation;
    }

    private bool TryResolve(EdgeHandle handle, out int index)
    {
        index = handle.Index;
        return handle.GraphId == _graphId
            && (uint)index < (uint)_edgeHighWater
            && _edges[index].Alive
            && _edges[index].Generation == handle.Generation;
    }
}
