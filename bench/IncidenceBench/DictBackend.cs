// Backend 2: pure dictionary-keyed.
// Node store, edge store, and per-node incidence are all Dictionary-keyed by an
// opaque monotonic long id. Trivial removal; worse cache locality and higher
// per-entry overhead than arrays. A liveIds list backs random sampling (arrays
// get enumeration/sampling for free; dictionaries pay for it) — churn only.

sealed class DictBackend : IBackend
{
    public string Name => "Dict";

    sealed class NodeEntry
    {
        public int Payload;
        public List<long> Out = new();
        public List<long> In = new();
    }

    sealed class EdgeEntry
    {
        public long Src;
        public long Dst;
        public int Payload;
    }

    readonly Dictionary<long, NodeEntry> _nodes = new();
    readonly Dictionary<long, EdgeEntry> _edges = new();
    readonly List<long> _liveIds = new();   // sampling scratch (churn only)
    long _nextNode = 1, _nextEdge = 1;

    public void Reserve(int nodes, int edges)
    {
        _nodes.EnsureCapacity(nodes);
        _edges.EnsureCapacity(edges);
        _liveIds.Capacity = nodes;
    }

    public NodeHandle AddNode(int payload)
    {
        long id = _nextNode++;
        _nodes[id] = new NodeEntry { Payload = payload };
        _liveIds.Add(id);
        return new NodeHandle(id);
    }

    public EdgeHandle AddEdge(NodeHandle src, NodeHandle dst, int payload)
    {
        long id = _nextEdge++;
        _edges[id] = new EdgeEntry { Src = src.Bits, Dst = dst.Bits, Payload = payload };
        _nodes[src.Bits].Out.Add(id);
        _nodes[dst.Bits].In.Add(id);
        return new EdgeHandle(id);
    }

    public void RemoveEdge(EdgeHandle e)
    {
        if (!_edges.TryGetValue(e.Bits, out var rec)) return;
        if (_nodes.TryGetValue(rec.Src, out var s)) s.Out.Remove(e.Bits);
        if (_nodes.TryGetValue(rec.Dst, out var d)) d.In.Remove(e.Bits);
        _edges.Remove(e.Bits);
    }

    public void RemoveNode(NodeHandle n)
    {
        if (!_nodes.TryGetValue(n.Bits, out var entry)) return;
        foreach (var eid in entry.Out)
        {
            if (_edges.TryGetValue(eid, out var rec))
            {
                if (rec.Dst != n.Bits && _nodes.TryGetValue(rec.Dst, out var d)) d.In.Remove(eid);
                _edges.Remove(eid);
            }
        }
        foreach (var eid in entry.In)
        {
            if (_edges.TryGetValue(eid, out var rec))
            {
                if (rec.Src != n.Bits && _nodes.TryGetValue(rec.Src, out var s)) s.Out.Remove(eid);
                _edges.Remove(eid);
            }
        }
        _nodes.Remove(n.Bits);
    }

    public long SweepOutIncidence()
    {
        long sum = 0;
        foreach (var entry in _nodes.Values)
        {
            var outL = entry.Out;
            for (int j = 0; j < outL.Count; j++)
            {
                var rec = _edges[outL[j]];
                sum += _nodes[rec.Dst].Payload;
            }
        }
        return sum;
    }

    public long SweepInIncidence()
    {
        long sum = 0;
        foreach (var entry in _nodes.Values)
        {
            var inL = entry.In;
            for (int j = 0; j < inL.Count; j++)
            {
                var rec = _edges[inL[j]];
                sum += _nodes[rec.Src].Payload;
            }
        }
        return sum;
    }

    public int RemoveSomeNodes(int k, Random r, NodeHandle[] outRemoved)
    {
        int got = 0, tries = 0, maxTries = k * 20 + 100;
        while (got < k && tries++ < maxTries)
        {
            long id = _liveIds[r.Next(_liveIds.Count)];
            if (!_nodes.ContainsKey(id)) continue;
            outRemoved[got++] = new NodeHandle(id);
            RemoveNode(outRemoved[got - 1]);
        }
        return got;
    }

    public NodeHandle? AnyLiveNode(Random r)
    {
        for (int t = 0; t < 40; t++)
        {
            long id = _liveIds[r.Next(_liveIds.Count)];
            if (_nodes.ContainsKey(id)) return new NodeHandle(id);
        }
        return null;
    }
}
