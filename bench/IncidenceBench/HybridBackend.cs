// Backend 3: hybrid.
// Node & edge PAYLOAD stores are array-backed generational slot-maps (dense,
// cache-friendly bulk/traversal access). Incidence indices are Dictionary
// side-structures keyed by node slot — so only nodes that actually have edges
// pay for a List, which suits a sparse graph with capacity headroom.

sealed class HybridBackend : IBackend
{
    public string Name => "Hybrid";

    // node slots (array-backed)
    int[] _nGen = new int[4];
    int[] _nPayload = new int[4];
    bool[] _nAlive = new bool[4];
    int[] _nNextFree = new int[4];
    int _nTop, _nFreeHead = -1;

    // edge slots (array-backed)
    int[] _eGen = new int[4];
    int[] _eSrc = new int[4];
    int[] _eDst = new int[4];
    int[] _ePayload = new int[4];
    bool[] _eAlive = new bool[4];
    int[] _eNextFree = new int[4];
    int _eTop, _eFreeHead = -1;

    // incidence as dictionary side-structures (node slot -> edge slots)
    readonly Dictionary<int, List<int>> _out = new();
    readonly Dictionary<int, List<int>> _in = new();

    static long Pack(int gen, int idx) => ((long)gen << 32) | (uint)idx;
    static int Idx(long bits) => (int)(bits & 0xFFFFFFFF);

    public void Reserve(int nodes, int edges)
    {
        GrowNodes(nodes);
        GrowEdges(edges);
        _out.EnsureCapacity(nodes);
        _in.EnsureCapacity(nodes);
    }

    void GrowNodes(int cap)
    {
        if (cap <= _nGen.Length) return;
        Array.Resize(ref _nGen, cap);
        Array.Resize(ref _nPayload, cap);
        Array.Resize(ref _nAlive, cap);
        Array.Resize(ref _nNextFree, cap);
    }

    void GrowEdges(int cap)
    {
        if (cap <= _eGen.Length) return;
        Array.Resize(ref _eGen, cap);
        Array.Resize(ref _eSrc, cap);
        Array.Resize(ref _eDst, cap);
        Array.Resize(ref _ePayload, cap);
        Array.Resize(ref _eAlive, cap);
        Array.Resize(ref _eNextFree, cap);
    }

    public NodeHandle AddNode(int payload)
    {
        int idx;
        if (_nFreeHead != -1) { idx = _nFreeHead; _nFreeHead = _nNextFree[idx]; }
        else { if (_nTop >= _nGen.Length) GrowNodes(_nGen.Length * 2); idx = _nTop++; }
        _nGen[idx]++;
        _nPayload[idx] = payload;
        _nAlive[idx] = true;
        return new NodeHandle(Pack(_nGen[idx], idx));
    }

    public EdgeHandle AddEdge(NodeHandle src, NodeHandle dst, int payload)
    {
        int s = Idx(src.Bits), d = Idx(dst.Bits);
        int idx;
        if (_eFreeHead != -1) { idx = _eFreeHead; _eFreeHead = _eNextFree[idx]; }
        else { if (_eTop >= _eGen.Length) GrowEdges(_eGen.Length * 2); idx = _eTop++; }
        _eGen[idx]++;
        _eSrc[idx] = s; _eDst[idx] = d; _ePayload[idx] = payload; _eAlive[idx] = true;
        Bucket(_out, s).Add(idx);
        Bucket(_in, d).Add(idx);
        return new EdgeHandle(Pack(_eGen[idx], idx));
    }

    static List<int> Bucket(Dictionary<int, List<int>> map, int key)
    {
        if (!map.TryGetValue(key, out var list)) { list = new List<int>(); map[key] = list; }
        return list;
    }

    void FreeEdgeSlot(int e)
    {
        _eAlive[e] = false;
        _eGen[e]++;
        _eNextFree[e] = _eFreeHead;
        _eFreeHead = e;
    }

    static void RemoveFromBucket(Dictionary<int, List<int>> map, int key, int val)
    {
        if (map.TryGetValue(key, out var list))
        {
            list.Remove(val);
            if (list.Count == 0) map.Remove(key);
        }
    }

    public void RemoveEdge(EdgeHandle e)
    {
        int idx = Idx(e.Bits);
        if (!_eAlive[idx]) return;
        RemoveFromBucket(_out, _eSrc[idx], idx);
        RemoveFromBucket(_in, _eDst[idx], idx);
        FreeEdgeSlot(idx);
    }

    public void RemoveNode(NodeHandle n)
    {
        int idx = Idx(n.Bits);
        if (!_nAlive[idx]) return;
        if (_out.TryGetValue(idx, out var outL))
        {
            for (int i = 0; i < outL.Count; i++)
            {
                int e = outL[i];
                if (!_eAlive[e]) continue;
                int d = _eDst[e];
                if (d != idx) RemoveFromBucket(_in, d, e);
                FreeEdgeSlot(e);
            }
            _out.Remove(idx);
        }
        if (_in.TryGetValue(idx, out var inL))
        {
            for (int i = 0; i < inL.Count; i++)
            {
                int e = inL[i];
                if (!_eAlive[e]) continue;
                int s = _eSrc[e];
                if (s != idx) RemoveFromBucket(_out, s, e);
                FreeEdgeSlot(e);
            }
            _in.Remove(idx);
        }
        _nAlive[idx] = false;
        _nGen[idx]++;
        _nNextFree[idx] = _nFreeHead;
        _nFreeHead = idx;
    }

    public long SweepOutIncidence()
    {
        long sum = 0;
        for (int i = 0; i < _nTop; i++)
        {
            if (!_nAlive[i]) continue;
            if (!_out.TryGetValue(i, out var list)) continue;
            for (int j = 0; j < list.Count; j++)
                sum += _nPayload[_eDst[list[j]]];
        }
        return sum;
    }

    public long SweepInIncidence()
    {
        long sum = 0;
        for (int i = 0; i < _nTop; i++)
        {
            if (!_nAlive[i]) continue;
            if (!_in.TryGetValue(i, out var list)) continue;
            for (int j = 0; j < list.Count; j++)
                sum += _nPayload[_eSrc[list[j]]];
        }
        return sum;
    }

    public int RemoveSomeNodes(int k, Random r, NodeHandle[] outRemoved)
    {
        int got = 0, tries = 0, maxTries = k * 20 + 100;
        while (got < k && tries++ < maxTries)
        {
            int idx = r.Next(_nTop);
            if (!_nAlive[idx]) continue;
            outRemoved[got++] = new NodeHandle(Pack(_nGen[idx], idx));
            RemoveNode(outRemoved[got - 1]);
        }
        return got;
    }

    public NodeHandle? AnyLiveNode(Random r)
    {
        for (int t = 0; t < 40; t++)
        {
            int idx = r.Next(_nTop);
            if (_nAlive[idx]) return new NodeHandle(Pack(_nGen[idx], idx));
        }
        return null;
    }
}
