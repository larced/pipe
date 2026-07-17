using GraphLibrary;
using GraphLibrary.Traversal;

namespace GraphLibrary.Tests;

// Seam — advisory cycle queries, ticket 12 (#23): IsAcyclic / FindCycles / WouldCreateCycle over the
// layered GraphLibrary.Traversal surface (spec story 20), reusing ticket 07's internal cycle detection.
// Available without attaching an acyclicity validator; FindCycles returns a witness set as closed Paths;
// results are eager snapshots.
public class CyclesTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    // --- IsAcyclic: the advisory read of acyclicity, no validator attached ---

    [Fact]
    public void IsAcyclic_TrueForADag_WithoutAnyValidator()
    {
        var graph = new Graph<Product, Dependency>(); // note: no TopologyValidator.Acyclic enabled
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.True(graph.IsAcyclic());
    }

    [Fact]
    public void IsAcyclic_FalseWhenACycleIsPresent()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));

        Assert.False(graph.IsAcyclic());
    }

    [Fact]
    public void IsAcyclic_FalseForASelfLoop()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.AddEdge(a, a, new Dependency("loop"));

        Assert.False(graph.IsAcyclic());
    }

    // --- FindCycles: a witness set of closed Paths ---

    [Fact]
    public void FindCycles_IsEmptyForAnAcyclicGraph()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Empty(graph.FindCycles());
    }

    [Fact]
    public void FindCycles_ReturnsAClosedPathForACycle()
    {
        // a -> b -> c -> a. The cycle is a closed Path: Start == Target and it walks every edge once.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));
        graph.AddEdge(c, a, new Dependency("3"));

        var cycle = Assert.Single(graph.FindCycles());

        Assert.Equal(cycle.Start, cycle.Target);           // closed
        Assert.Equal(3, cycle.Length);                     // three edges
        Assert.Equal(cycle.Start, graph.GetSource(cycle.Edges[0]));
        Assert.Equal(cycle.Target, graph.GetTarget(cycle.Edges[^1]));
        // Consecutive edges chain: each edge's target is the next edge's source.
        for (int i = 1; i < cycle.Edges.Count; i++)
        {
            Assert.Equal(graph.GetTarget(cycle.Edges[i - 1]), graph.GetSource(cycle.Edges[i]));
        }
        Assert.Equal(new[] { a, b, c }, CycleNodes(graph, cycle).OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void FindCycles_ReturnsASelfLoopAsALengthOneCycle()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var loop = graph.AddEdge(a, a, new Dependency("loop"));

        var cycle = Assert.Single(graph.FindCycles());
        Assert.Equal(a, cycle.Start);
        Assert.Equal(a, cycle.Target);
        Assert.Equal(new[] { loop }, cycle.Edges.ToArray());
    }

    [Fact]
    public void FindCycles_UnderParallelBackEdges_NamesTheSpecificEdges()
    {
        // a -> b with two parallel b -> a edges: two witness cycles, each naming one back edge.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var ab = graph.AddEdge(a, b, new Dependency("1"));
        var ba1 = graph.AddEdge(b, a, new Dependency("2"));
        var ba2 = graph.AddEdge(b, a, new Dependency("3"));

        var cycles = graph.FindCycles();

        Assert.Equal(2, cycles.Count);
        Assert.All(cycles, cyc => Assert.Contains(ab, cyc.Edges));
        Assert.Contains(cycles, cyc => cyc.Edges.Contains(ba1));
        Assert.Contains(cycles, cyc => cyc.Edges.Contains(ba2));
    }

    [Fact]
    public void FindCycles_IsNonEmptyExactlyWhenNotAcyclic()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));      // acyclic so far
        Assert.Equal(graph.IsAcyclic(), graph.FindCycles().Count == 0);

        graph.AddEdge(b, c, new Dependency("2"));
        graph.AddEdge(c, b, new Dependency("3"));      // now a cycle b <-> c
        Assert.False(graph.IsAcyclic());
        Assert.NotEmpty(graph.FindCycles());
    }

    // --- WouldCreateCycle: the advisory pre-check ---

    [Fact]
    public void WouldCreateCycle_TrueWhenTargetAlreadyReachesSource()
    {
        // a -> b -> c. Adding c -> a would close a loop because a already reaches c.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.True(graph.WouldCreateCycle(c, a));
        Assert.False(graph.WouldCreateCycle(a, c)); // a -> c parallels the existing route, no loop
    }

    [Fact]
    public void WouldCreateCycle_TrueForASelfEdge()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));

        Assert.True(graph.WouldCreateCycle(a, a));
    }

    [Fact]
    public void WouldCreateCycle_DoesNotMutateTheGraph()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        graph.WouldCreateCycle(b, a);

        Assert.True(graph.IsAcyclic());            // advisory only — still acyclic
        Assert.Empty(graph.GetEdges(b, a));        // no edge was added
    }

    [Fact]
    public void WouldCreateCycle_StaleEndpoint_ThrowsOnCall()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.RemoveNode(b);

        Assert.Throws<InvalidHandleException>(() => graph.WouldCreateCycle(a, b));
    }

    // --- Eager snapshot & argument guards ---

    [Fact]
    public void FindCycles_IsAnEagerSnapshot_StableAcrossLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));

        var cycles = graph.FindCycles();
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, c, new Dependency("3"));

        Assert.Single(cycles); // computed before the new node/edge; unaffected
    }

    [Fact]
    public void IsAcyclic_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.IsAcyclic());
    }

    [Fact]
    public void FindCycles_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.FindCycles());
    }

    [Fact]
    public void WouldCreateCycle_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.WouldCreateCycle(default, default));
    }

    // The distinct nodes a closed cycle path visits: its start, then the target of each edge.
    private static IEnumerable<NodeHandle> CycleNodes(
        IReadableGraph<Product, Dependency> graph, Traversal.Path cycle)
    {
        var nodes = new HashSet<NodeHandle> { cycle.Start };
        foreach (EdgeHandle edge in cycle.Edges)
        {
            nodes.Add(graph.GetTarget(edge));
        }
        return nodes;
    }

    private static Func<NodeHandle, string> Name(IReadableGraph<Product, Dependency> graph) =>
        n => graph.GetNodePayload(n).Name;
}
