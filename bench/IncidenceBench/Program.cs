using System.Diagnostics;

// Throwaway benchmark for wayfinder ticket #8:
// physical backing container for the incidence-list store (settled in #5).
// Model: node store + central edge store + per-node in/out incidence indices.
// Identity is an OPAQUE HANDLE (a long, wrapped), never the payload (invariant from #5).
//
// Three backends, isolating two axes:
//   1. SlotMap   - generational arrays for node store, edge store, and per-node
//                  incidence (List<int> in a parallel array). Pure array-handle.
//   2. Dict      - Dictionary for node store, edge store, and incidence. Pure dict.
//   3. Hybrid    - array-backed (slot-map) node & edge PAYLOAD stores for
//                  cache-friendly bulk access, but Dictionary-keyed incidence
//                  side-structures (only edged nodes pay). Targets sparsity.

const int AvgOutDegree = 8;    // sparse
const int Seed = 1234;
int[] scales = { 1_000, 10_000, 25_000, 50_000 };

Console.WriteLine($"avg out-degree {AvgOutDegree} (sparse). .NET {Environment.Version}, GC={GCMode()}");

Func<IBackend>[] factories =
{
    () => new SlotMapBackend(),
    () => new DictBackend(),
    () => new HybridBackend(),
};

foreach (int Nodes in scales)
{
    int Edges = Nodes * AvgOutDegree;

    // Fixed reproducible edge plan: node ordinals in [0, Nodes). Slightly skewed so
    // degree isn't perfectly uniform (a few hubs), which is realistic for real graphs.
    var rng = new Random(Seed);
    var edgeSrc = new int[Edges];
    var edgeDst = new int[Edges];
    for (int i = 0; i < Edges; i++)
    {
        edgeSrc[i] = SkewedNode(rng, Nodes);
        edgeDst[i] = SkewedNode(rng, Nodes);
    }

    Console.WriteLine();
    Console.WriteLine($"=== {Nodes:N0} nodes => {Edges:N0} edges ===");
    Console.WriteLine($"{"Backend",-10} | {"Build",8} | {"OutSweep",9} | {"InSweep",8} | {"AddEdge",8} | {"RmEdge",8} | {"RmNode",8} | {"Mem",8} | {"MemChurn",9}");
    Console.WriteLine(new string('-', 104));

    foreach (var factory in factories)
    {
        var name = factory().Name;

        double buildMs = TimeFreshBuild(factory, edgeSrc, edgeDst, Nodes, out var built);

        long checksum = 0;
        double outSweepMs = TimeRepeat(() => checksum += built.SweepOutIncidence(), 8);
        double inSweepMs = TimeRepeat(() => checksum += built.SweepInIncidence(), 8);

        double memMB = MeasureFootprintMB(factory, edgeSrc, edgeDst, Nodes, churnCycles: 0, out _);
        double memChurnMB = MeasureFootprintMB(factory, edgeSrc, edgeDst, Nodes, churnCycles: 10, out var churned);

        double addEdgeNs = TimeAddEdges(factory, edgeSrc, edgeDst, Nodes);
        double rmEdgeNs = TimeRemoveEdges(factory, edgeSrc, edgeDst, Nodes);
        double rmNodeNs = TimeRemoveNodes(factory, edgeSrc, edgeDst, Nodes);

        Console.WriteLine($"{name,-10} | {buildMs,6:F1}ms | {outSweepMs,7:F3}ms | {inSweepMs,6:F3}ms | {addEdgeNs,5:F1}ns | {rmEdgeNs,5:F1}ns | {rmNodeNs,5:F1}ns | {memMB,5:F2}MB | {memChurnMB,6:F2}MB");
        GC.KeepAlive(checksum);
        GC.KeepAlive(built);
        GC.KeepAlive(churned);
    }
}

Console.WriteLine();
Console.WriteLine("Build/sweeps: lower=faster. Add/Rm: ns per op. Mem: retained after build. MemChurn: retained after 10 remove/re-add cycles.");

static string GCMode() => System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation";

static int SkewedNode(Random rng, int n)
{
    if (rng.NextDouble() < 0.15) return rng.Next(0, Math.Max(1, n / 20)); // hubs
    return rng.Next(0, n);
}

// ---- Timing helpers ----

static double TimeFreshBuild(Func<IBackend> factory, int[] es, int[] ed, int nodes, out IBackend last)
{
    last = default!;
    for (int w = 0; w < 2; w++) { var g = factory(); Build(g, es, ed, nodes); }
    double best = double.MaxValue;
    for (int it = 0; it < 5; it++)
    {
        var g = factory();
        GcQuiesce();
        var sw = Stopwatch.StartNew();
        Build(g, es, ed, nodes);
        sw.Stop();
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        last = g;
    }
    return best;
}

static void Build(IBackend g, int[] es, int[] ed, int nodes)
{
    g.Reserve(nodes, es.Length);
    var handles = new NodeHandle[nodes];
    for (int i = 0; i < nodes; i++) handles[i] = g.AddNode(i);
    for (int i = 0; i < es.Length; i++) g.AddEdge(handles[es[i]], handles[ed[i]], i);
}

static double TimeRepeat(Action a, int iters)
{
    a(); a();
    double best = double.MaxValue;
    for (int it = 0; it < iters; it++)
    {
        GcQuiesce();
        var sw = Stopwatch.StartNew();
        a();
        sw.Stop();
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    return best;
}

static double TimeAddEdges(Func<IBackend> factory, int[] es, int[] ed, int nodes)
{
    for (int w = 0; w < 2; w++) { var gw = factory(); AddNodesOnly(gw, nodes, out _); }
    double best = double.MaxValue;
    for (int it = 0; it < 4; it++)
    {
        var g = factory();
        AddNodesOnly(g, nodes, out var handles);
        GcQuiesce();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < es.Length; i++) g.AddEdge(handles[es[i]], handles[ed[i]], i);
        sw.Stop();
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    return best * 1_000_000.0 / es.Length;
}

static void AddNodesOnly(IBackend g, int nodes, out NodeHandle[] handles)
{
    g.Reserve(nodes, nodes * 8);
    handles = new NodeHandle[nodes];
    for (int i = 0; i < nodes; i++) handles[i] = g.AddNode(i);
}

static double TimeRemoveEdges(Func<IBackend> factory, int[] es, int[] ed, int nodes)
{
    int removeCount = es.Length / 4;
    double best = double.MaxValue;
    for (int it = 0; it < 4; it++)
    {
        var g = factory();
        var eh = BuildReturningEdgeHandles(g, es, ed, nodes);
        var order = Shuffle(eh.Length, Seed + it);
        GcQuiesce();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < removeCount; i++) g.RemoveEdge(eh[order[i]]);
        sw.Stop();
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    return best * 1_000_000.0 / removeCount;
}

static double TimeRemoveNodes(Func<IBackend> factory, int[] es, int[] ed, int nodes)
{
    int removeCount = nodes / 4;
    double best = double.MaxValue;
    for (int it = 0; it < 4; it++)
    {
        var g = factory();
        var nh = BuildReturningNodeHandles(g, es, ed, nodes);
        var order = Shuffle(nh.Length, Seed + 99 + it);
        GcQuiesce();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < removeCount; i++) g.RemoveNode(nh[order[i]]);
        sw.Stop();
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    return best * 1_000_000.0 / removeCount;
}

static EdgeHandle[] BuildReturningEdgeHandles(IBackend g, int[] es, int[] ed, int nodes)
{
    g.Reserve(nodes, es.Length);
    var handles = new NodeHandle[nodes];
    for (int i = 0; i < nodes; i++) handles[i] = g.AddNode(i);
    var eh = new EdgeHandle[es.Length];
    for (int i = 0; i < es.Length; i++) eh[i] = g.AddEdge(handles[es[i]], handles[ed[i]], i);
    return eh;
}

static NodeHandle[] BuildReturningNodeHandles(IBackend g, int[] es, int[] ed, int nodes)
{
    g.Reserve(nodes, es.Length);
    var handles = new NodeHandle[nodes];
    for (int i = 0; i < nodes; i++) handles[i] = g.AddNode(i);
    for (int i = 0; i < es.Length; i++) g.AddEdge(handles[es[i]], handles[ed[i]], i);
    return handles;
}

static int[] Shuffle(int n, int seed)
{
    var a = new int[n];
    for (int i = 0; i < n; i++) a[i] = i;
    var r = new Random(seed);
    for (int i = n - 1; i > 0; i--) { int j = r.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
    return a;
}

static double MeasureFootprintMB(Func<IBackend> factory, int[] es, int[] ed, int nodes, int churnCycles, out IBackend graph)
{
    GcQuiesce();
    long before = GC.GetTotalMemory(true);
    var g = factory();
    Build(g, es, ed, nodes);
    if (churnCycles > 0) Churn(g, es, ed, nodes, churnCycles);
    GcQuiesce();
    long after = GC.GetTotalMemory(true);
    graph = g;
    GC.KeepAlive(g);
    return (after - before) / (1024.0 * 1024.0);
}

static void Churn(IBackend g, int[] es, int[] ed, int nodes, int cycles)
{
    var r = new Random(Seed + 7);
    for (int c = 0; c < cycles; c++)
    {
        int k = nodes / 10;
        var removed = new NodeHandle[k];
        int got = g.RemoveSomeNodes(k, r, removed);
        for (int i = 0; i < got; i++)
        {
            var h = g.AddNode(1_000_000 + c * nodes + i);
            for (int e = 0; e < 8; e++)
            {
                var other = g.AnyLiveNode(r);
                if (other.HasValue) g.AddEdge(h, other.Value, -1);
            }
        }
    }
}

static void GcQuiesce()
{
    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
}

// ======================= Contract =======================

readonly struct NodeHandle
{
    public readonly long Bits;
    public NodeHandle(long bits) => Bits = bits;
}

readonly struct EdgeHandle
{
    public readonly long Bits;
    public EdgeHandle(long bits) => Bits = bits;
}

interface IBackend
{
    string Name { get; }
    void Reserve(int nodes, int edges);
    NodeHandle AddNode(int payload);
    EdgeHandle AddEdge(NodeHandle src, NodeHandle dst, int payload);
    void RemoveEdge(EdgeHandle e);
    void RemoveNode(NodeHandle n);            // cascades incident edges
    long SweepOutIncidence();                 // checksum over every node's out-edges' target payloads
    long SweepInIncidence();
    int RemoveSomeNodes(int k, Random r, NodeHandle[] outRemoved);
    NodeHandle? AnyLiveNode(Random r);
}
