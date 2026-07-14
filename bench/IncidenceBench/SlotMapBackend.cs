// Backend 1: pure generational slot-map.
// Node store, edge store, and per-node in/out incidence all live in
// contiguous arrays indexed by dense slot index; free-lists reclaim slots;
// generation counters ride in the opaque handle to invalidate stale handles.

sealed class SlotMapBackend : IBackend
{
    public string Name => "SlotMap";

    // node slots
    int[] _nGen = new int[4];
    int[] _nPayload = new int[4];
    List<int>[] _out = new List<int>[4];   // edge slot indices
    List<int>[] _in = new List<int>[4];
    bool[] _nAlive = new bool[4];
    int[] _nNextFree = new int[4];
    int _nTop, _nFreeHead = -1;

    // edge slots
    int[] _eGen = new int[4];
    int[] _eSrc = new int[4];   // node slot index
    int[] _eDst = new int[4];
    int[] _ePayload = new int[4];
    bool[] _eAlive = new bool[4];
    int[] _eNextFree = new int[4];
    int _eTop, _eFreeHead = -1;

    static long Pack(int gen, int idx) => ((long)gen << 32) | (uint)idx;
    static int Idx(long bits) => (int)(bits & 0xFFFFFFFF);

    public void Reserve(int nodes, int edges)
    {
        GrowNodes(nodes);
        GrowEdges(edges);
    }

    void GrowNodes(int cap)
    {
        if (cap <= _nGen.Length) return;
        Array.Resize(ref _nGen, cap);
        Array.Resize(ref _nPayload, cap);
        Array.Resize(ref _out, cap);
        Array.Resize(ref _in, cap);
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
        (_out[idx] ??= new List<int>()).Clear();
        (_in[idx] ??= new List<int>()).Clear();
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
        _out[s].Add(idx);
        _in[d].Add(idx);
        return new EdgeHandle(Pack(_eGen[idx], idx));
    }

    void FreeEdgeSlot(int e)
    {
        _eAlive[e] = false;
        _eGen[e]++;
        _eNextFree[e] = _eFreeHead;
        _eFreeHead = e;
    }

    public void RemoveEdge(EdgeHandle e)
    {
        int idx = Idx(e.Bits);
        if (!_eAlive[idx]) return;
        _out[_eSrc[idx]].Remove(idx);
        _in[_eDst[idx]].Remove(idx);
        FreeEdgeSlot(idx);
    }

    public void RemoveNode(NodeHandle n)
    {
        int idx = Idx(n.Bits);
        if (!_nAlive[idx]) return;
        var outL = _out[idx];
        for (int i = 0; i < outL.Count; i++)
        {
            int e = outL[i];
            if (!_eAlive[e]) continue;
            int d = _eDst[e];
            if (d != idx) _in[d].Remove(e);
            FreeEdgeSlot(e);
        }
        var inL = _in[idx];
        for (int i = 0; i < inL.Count; i++)
        {
            int e = inL[i];
            if (!_eAlive[e]) continue;   // may already be freed (self-loop)
            int s = _eSrc[e];
            if (s != idx) _out[s].Remove(e);
            FreeEdgeSlot(e);
        }
        outL.Clear();
        inL.Clear();
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
            var list = _out[i];
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
            var list = _in[i];
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
