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
/// #13 (on top of #12's add &amp; read) made the graph mutable-consistent:
/// <see cref="RemoveNode"/> sweeps a node and every incident edge, <see cref="RemoveEdge"/>
/// retracts one edge and leaves its endpoints, and per-node in/out incidence indices make both
/// traversal directions and degree cheap. This slice (#14) adds the runtime handle guard: every
/// handle-consuming member resolves through the generation/graph stamp, so a handle from another
/// graph or one whose element was removed throws <see cref="InvalidHandleException"/> (ADR 0003)
/// instead of aliasing the wrong slot. Payload mutation, validators, and the fluent builder arrive
/// in later tickets.
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
    // Distinct id per graph instance, stamped into every handle this graph mints so RequireOwnGraph
    // can reject a handle used against the wrong graph. Starts at 1 (Interlocked.Increment from 0),
    // so a default handle's id of 0 never matches a real graph. Interlocked keeps ids unique even if
    // graphs are constructed concurrently (construction itself needs no other sync).
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

    /// <summary>
    /// The observable node-payload change channel (ADR 0002/0003). Fires once per
    /// <see cref="SetPayload(NodeHandle,TNode)"/> that hits a live node, carrying the handle plus
    /// its old and new payload — enough for an opt-in secondary index to re-key content without the
    /// core carrying any content index of its own. Off by default: with no subscriber it costs nothing.
    /// </summary>
    public event Action<PayloadChange<NodeHandle, TNode>>? NodePayloadChanged;

    /// <summary>
    /// The observable edge-payload change channel (ADR 0002/0003). Fires once per
    /// <see cref="SetPayload(EdgeHandle,TEdge)"/> that hits a live edge, carrying the handle plus its
    /// old and new payload. The edge counterpart of <see cref="NodePayloadChanged"/>.
    /// </summary>
    public event Action<PayloadChange<EdgeHandle, TEdge>>? EdgePayloadChanged;

    /// <summary>
    /// Creates an opt-in <see cref="SecondaryIndex{TKey}"/> mapping the key
    /// <paramref name="keySelector"/> derives from each node's payload back to that node's handle,
    /// for content-based lookup (CONTEXT.md → Secondary index, spec stories 42-44). The index is
    /// seeded from the nodes present now and then kept correct across payload mutation by subscribing
    /// to <see cref="NodePayloadChanged"/> — so content lookups never go stale as payloads change.
    /// </summary>
    /// <remarks>
    /// Off by default and not part of core identity (ADR 0003): the core holds no content index, so a
    /// graph you never call this on stays lean and domain-agnostic (story 44). Call it once per key you
    /// want to look up by; several independent indexes over one graph are each maintained. Dispose the
    /// returned index to detach it from the channel when you no longer need it. Only <c>SetPayload</c>
    /// re-keys the index; nodes added after this call are not indexed (there is no structural channel).
    /// </remarks>
    public SecondaryIndex<TKey> IndexNodesBy<TKey>(Func<TNode, TKey> keySelector)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        var index = new SecondaryIndex<TKey>();

        // Seed from the nodes present at creation. GetNodePayload here is a plain live read.
        foreach (NodeHandle handle in Nodes)
        {
            index.Add(keySelector(GetNodePayload(handle)), handle);
        }

        // Track payload mutation off the change channel: retire the old key and register the new one,
        // both against the unchanged handle. A mutation that leaves the key untouched is a no-op, so an
        // off-key payload edit costs the index nothing. The keySelector closure is what keeps TNode off
        // SecondaryIndex's signature (ADR 0003 — handles and the index stay non-generic in TNode/TEdge).
        void OnNodePayloadChanged(PayloadChange<NodeHandle, TNode> change)
        {
            TKey oldKey = keySelector(change.OldPayload);
            TKey newKey = keySelector(change.NewPayload);
            if (EqualityComparer<TKey>.Default.Equals(oldKey, newKey))
            {
                return;
            }
            index.Remove(oldKey, change.Handle);
            index.Add(newKey, change.Handle);
        }

        NodePayloadChanged += OnNodePayloadChanged;
        index.SetDetach(() => NodePayloadChanged -= OnNodePayloadChanged);
        return index;
    }

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
        // Endpoints must be live handles of this graph — a cross-graph or stale endpoint would
        // otherwise corrupt incidence by aliasing an unrelated (or freed) slot. Fail fast (ADR 0003).
        int sourceIndex = Resolve(source);
        int targetIndex = Resolve(target);

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
        _nodes[sourceIndex].OutEdges.Add(index);
        _nodes[targetIndex].InEdges.Add(index);

        return new EdgeHandle(index, slot.Generation, _graphId);
    }

    /// <summary>
    /// Replaces the payload of the node identified by <paramref name="handle"/> in place and
    /// broadcasts the change on <see cref="NodePayloadChanged"/>. The handle is never invalidated and
    /// graph structure is never disturbed — only the payload changes — so this is safe mid-traversal-
    /// planning (ADR 0003, spec story 10). A stale or cross-graph handle is a no-op that broadcasts
    /// nothing; the throwing stale-handle guard (<c>InvalidHandleException</c>) is a later ticket (ADR 0003).
    /// </summary>
    public void SetPayload(NodeHandle handle, TNode payload)
    {
        if (!TryResolve(handle, out int index))
        {
            return;
        }

        ref NodeSlot slot = ref _nodes[index];
        TNode old = slot.Payload;
        slot.Payload = payload;
        NodePayloadChanged?.Invoke(new PayloadChange<NodeHandle, TNode>(handle, old, payload));
    }

    /// <summary>
    /// Replaces the payload of the edge identified by <paramref name="handle"/> in place and
    /// broadcasts the change on <see cref="EdgePayloadChanged"/>. Endpoints and incidence are left
    /// untouched — only the payload changes (ADR 0003, spec story 10). A stale or cross-graph handle
    /// is a no-op that broadcasts nothing; the throwing guard is a later ticket.
    /// </summary>
    public void SetPayload(EdgeHandle handle, TEdge payload)
    {
        if (!TryResolve(handle, out int index))
        {
            return;
        }

        ref EdgeSlot slot = ref _edges[index];
        TEdge old = slot.Payload;
        slot.Payload = payload;
        EdgePayloadChanged?.Invoke(new PayloadChange<EdgeHandle, TEdge>(handle, old, payload));
    }

    /// <summary>
    /// Removes the node identified by <paramref name="handle"/> and every edge incident to it
    /// (in and out), with no manual bookkeeping required. Returns <see langword="true"/> if the
    /// node existed, <see langword="false"/> if it was already gone (idempotent).
    /// </summary>
    public bool RemoveNode(NodeHandle handle)
    {
        // A handle from another graph is unambiguous misuse, not "already removed" — throw. A stale
        // same-graph handle, though, means the node is genuinely gone, so removal stays idempotent.
        RequireOwnGraph(handle.GraphId, "node");
        if (!IsLiveNode(handle))
        {
            return false;
        }

        int index = handle.Index;
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
        // Same posture as RemoveNode: cross-graph misuse throws, a stale same-graph handle is
        // idempotent (the edge — including one swept by a node removal — is genuinely gone).
        RequireOwnGraph(handle.GraphId, "edge");
        if (!IsLiveEdge(handle))
        {
            return false;
        }

        RetractEdge(handle.Index);
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
    public TNode GetNodePayload(NodeHandle handle) => _nodes[Resolve(handle)].Payload;

    /// <inheritdoc/>
    public TEdge GetEdgePayload(EdgeHandle handle) => _edges[Resolve(handle)].Payload;

    /// <inheritdoc/>
    public NodeHandle GetSource(EdgeHandle handle) => _edges[Resolve(handle)].Source;

    /// <inheritdoc/>
    public NodeHandle GetTarget(EdgeHandle handle) => _edges[Resolve(handle)].Target;

    // The guard is resolved eagerly (the Resolve call is an argument, evaluated before the iterator
    // is built), so an invalid handle throws on the call rather than on deferred enumeration.
    /// <inheritdoc/>
    public IEnumerable<EdgeHandle> GetOutEdges(NodeHandle node) => IncidenceOf(Resolve(node), outgoing: true);

    /// <inheritdoc/>
    public IEnumerable<EdgeHandle> GetInEdges(NodeHandle node) => IncidenceOf(Resolve(node), outgoing: false);

    private IEnumerable<EdgeHandle> IncidenceOf(int index, bool outgoing)
    {
        List<int> edges = outgoing ? _nodes[index].OutEdges : _nodes[index].InEdges;
        foreach (int edgeIndex in edges)
        {
            yield return new EdgeHandle(edgeIndex, _edges[edgeIndex].Generation, _graphId);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<EdgeHandle> GetEdges(NodeHandle source, NodeHandle target)
    {
        // Both endpoints are guarded (eagerly) so a misused handle fails fast, then edges are
        // derived by scanning the source's out-incidence and filtering on target — no dedicated
        // (source, target) index (ADR 0002); cheap when sparse.
        int sourceIndex = Resolve(source);
        Resolve(target);
        return EdgesBetween(sourceIndex, target);
    }

    private IEnumerable<EdgeHandle> EdgesBetween(int sourceIndex, NodeHandle target)
    {
        foreach (int edgeIndex in _nodes[sourceIndex].OutEdges)
        {
            if (_edges[edgeIndex].Target == target)
            {
                yield return new EdgeHandle(edgeIndex, _edges[edgeIndex].Generation, _graphId);
            }
        }
    }

    /// <inheritdoc/>
    public int GetOutDegree(NodeHandle node) => _nodes[Resolve(node)].OutEdges.Count;

    /// <inheritdoc/>
    public int GetInDegree(NodeHandle node) => _nodes[Resolve(node)].InEdges.Count;

    // Throwing guard (ADR 0003): resolves a handle to its live slot index, or fails fast — a handle
    // from another graph throws CrossGraph, one whose element was removed (its slot freed or reused
    // by a newer element, told apart by generation) throws Stale. This is what makes handle misuse
    // surface immediately instead of silently reading the wrong element or returning garbage.
    private int Resolve(NodeHandle handle)
    {
        RequireOwnGraph(handle.GraphId, "node");
        if (!IsLiveNode(handle))
        {
            throw InvalidHandleException.Stale("node");
        }
        return handle.Index;
    }

    private int Resolve(EdgeHandle handle)
    {
        RequireOwnGraph(handle.GraphId, "edge");
        if (!IsLiveEdge(handle))
        {
            throw InvalidHandleException.Stale("edge");
        }
        return handle.Index;
    }

    // Non-throwing resolve used on the SetPayload path (ticket 04), where a stale or cross-graph
    // handle is a silent no-op rather than an error — payload mutation broadcasts nothing when it
    // hits nothing. The throwing Resolve above is for read/incidence members; this is its lenient
    // sibling. Returns false (index unset) unless the handle is a live node of this graph.
    private bool TryResolve(NodeHandle handle, out int index)
    {
        if (handle.GraphId == _graphId && IsLiveNode(handle))
        {
            index = handle.Index;
            return true;
        }
        index = 0;
        return false;
    }

    private bool TryResolve(EdgeHandle handle, out int index)
    {
        if (handle.GraphId == _graphId && IsLiveEdge(handle))
        {
            index = handle.Index;
            return true;
        }
        index = 0;
        return false;
    }

    // Non-throwing liveness test used on the removal path, where absence is a normal outcome: it
    // keeps removals idempotent (a stale same-graph handle is a no-op, not an error). Cross-graph
    // rejection is handled separately by the caller via RequireOwnGraph before this runs.
    private bool IsLiveNode(NodeHandle handle)
    {
        int index = handle.Index;
        return (uint)index < (uint)_nodeHighWater
            && _nodes[index].Alive
            && _nodes[index].Generation == handle.Generation;
    }

    private bool IsLiveEdge(EdgeHandle handle)
    {
        int index = handle.Index;
        return (uint)index < (uint)_edgeHighWater
            && _edges[index].Alive
            && _edges[index].Generation == handle.Generation;
    }

    // A default/uninitialised handle carries graph id 0; real graph ids start at 1, so it is
    // rejected here as not belonging to this graph.
    private void RequireOwnGraph(int handleGraphId, string element)
    {
        if (handleGraphId != _graphId)
        {
            throw InvalidHandleException.CrossGraph(element);
        }
    }
}
