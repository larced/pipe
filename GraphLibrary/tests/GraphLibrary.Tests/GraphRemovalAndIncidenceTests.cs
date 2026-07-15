using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 13 (#13): removal, incidence cleanup, endpoint-pair
// access, and the IReadableGraph read surface. Every assertion drives only the public API;
// nothing reaches into the slot-map backing (ADR 0002/0003).
public class GraphRemovalAndIncidenceTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    // --- RemoveNode: node + all incident edges, no manual bookkeeping ---

    [Fact]
    public void RemoveNode_RemovesNodeAndAllIncidentEdges()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var ab = graph.AddEdge(a, b, new Dependency("out"));   // b is source-side of nothing; out of a
        var cb = graph.AddEdge(c, b, new Dependency("in"));    // into b
        var bc = graph.AddEdge(b, c, new Dependency("out2"));  // out of b

        var removed = graph.RemoveNode(b);

        Assert.True(removed);
        Assert.DoesNotContain(b, graph.Nodes);
        // Every edge touching b (in OR out) is gone; the edge not touching b survives.
        Assert.DoesNotContain(ab, graph.Edges);
        Assert.DoesNotContain(cb, graph.Edges);
        Assert.DoesNotContain(bc, graph.Edges);
        Assert.Empty(graph.Edges);
        // Surviving endpoints keep clean incidence — no dangling references to the removed edges.
        Assert.Empty(graph.GetOutEdges(a));
        Assert.Empty(graph.GetInEdges(c));
        Assert.Empty(graph.GetOutEdges(c));
    }

    [Fact]
    public void RemoveNode_LeavesUnrelatedNodesAndEdgesIntact()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        var cd = graph.AddEdge(c, d, new Dependency("keep"));
        graph.AddEdge(a, b, new Dependency("drop"));

        graph.RemoveNode(a);

        Assert.Contains(c, graph.Nodes);
        Assert.Contains(d, graph.Nodes);
        Assert.Contains(cd, graph.Edges);
        Assert.Equal(new[] { cd }, graph.GetOutEdges(c));
        Assert.Equal(new[] { cd }, graph.GetInEdges(d));
    }

    [Fact]
    public void RemoveNode_WithSelfLoop_RemovesTheLoopOnce()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var loop = graph.AddEdge(a, a, new Dependency("self"));

        var removed = graph.RemoveNode(a);

        Assert.True(removed);
        Assert.DoesNotContain(a, graph.Nodes);
        Assert.DoesNotContain(loop, graph.Edges);
        Assert.Empty(graph.Edges);
    }

    // --- RemoveEdge: retract one edge, endpoints untouched ---

    [Fact]
    public void RemoveEdge_RetractsSingleEdge_WithoutTouchingEndpoints()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));

        var removed = graph.RemoveEdge(edge);

        Assert.True(removed);
        Assert.DoesNotContain(edge, graph.Edges);
        // Endpoints survive, and their incidence is cleaned up on both sides.
        Assert.Contains(a, graph.Nodes);
        Assert.Contains(b, graph.Nodes);
        Assert.Empty(graph.GetOutEdges(a));
        Assert.Empty(graph.GetInEdges(b));
    }

    [Fact]
    public void RemoveEdge_LeavesParallelEdgesIntact()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var first = graph.AddEdge(a, b, new Dependency("1"));
        var second = graph.AddEdge(a, b, new Dependency("2"));

        graph.RemoveEdge(first);

        Assert.DoesNotContain(first, graph.Edges);
        Assert.Contains(second, graph.Edges);
        Assert.Equal(new[] { second }, graph.GetOutEdges(a));
        Assert.Equal(new[] { second }, graph.GetInEdges(b));
        Assert.Equal(new[] { second }, graph.GetEdges(a, b));
    }

    // --- Both removals: bool, idempotent cleanup ---

    [Fact]
    public void RemoveNode_ReturnsFalse_WhenAlreadyRemoved()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));

        Assert.True(graph.RemoveNode(a));
        Assert.False(graph.RemoveNode(a));   // idempotent: second call is a no-op returning false
    }

    [Fact]
    public void RemoveEdge_ReturnsFalse_WhenAlreadyRemoved()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));

        Assert.True(graph.RemoveEdge(edge));
        Assert.False(graph.RemoveEdge(edge));
    }

    [Fact]
    public void RemoveEdge_ReturnsFalse_WhenEdgeWasSweptByNodeRemoval()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));

        graph.RemoveNode(a);   // sweeps `edge` as incident

        Assert.False(graph.RemoveEdge(edge));
    }

    // --- GetEdges(a, b): endpoint-pair returns a collection ---

    [Fact]
    public void GetEdges_ReturnsAllParallelEdgesBetweenTheEndpoints()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var e1 = graph.AddEdge(a, b, new Dependency("1"));
        var e2 = graph.AddEdge(a, b, new Dependency("2"));

        var edges = graph.GetEdges(a, b).ToList();

        Assert.Equal(2, edges.Count);
        Assert.Contains(e1, edges);
        Assert.Contains(e2, edges);
    }

    [Fact]
    public void GetEdges_IsDirected_AndExcludesReverseAndUnrelatedEdges()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var ab = graph.AddEdge(a, b, new Dependency("ab"));
        graph.AddEdge(b, a, new Dependency("ba"));  // reverse — must not appear for (a,b)
        graph.AddEdge(a, c, new Dependency("ac"));  // different target

        Assert.Equal(new[] { ab }, graph.GetEdges(a, b));
    }

    [Fact]
    public void GetEdges_ReturnsEmpty_ForUnconnectedPair()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));

        Assert.Empty(graph.GetEdges(a, b));
    }

    // --- Incidence & degree, both directions ---

    [Fact]
    public void Incidence_RecordsBothDirections()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var ab = graph.AddEdge(a, b, new Dependency("ab"));

        Assert.Equal(new[] { ab }, graph.GetOutEdges(a));
        Assert.Empty(graph.GetInEdges(a));
        Assert.Equal(new[] { ab }, graph.GetInEdges(b));
        Assert.Empty(graph.GetOutEdges(b));
    }

    [Fact]
    public void Degree_CountsOutAndInEdges()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, c, new Dependency("2"));
        graph.AddEdge(c, a, new Dependency("3"));

        Assert.Equal(2, graph.GetOutDegree(a));
        Assert.Equal(1, graph.GetInDegree(a));
        Assert.Equal(0, graph.GetOutDegree(b));
        Assert.Equal(1, graph.GetInDegree(b));
    }

    [Fact]
    public void SelfLoop_CountsInBothIncidenceDirections()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var loop = graph.AddEdge(a, a, new Dependency("self"));

        Assert.Equal(new[] { loop }, graph.GetOutEdges(a));
        Assert.Equal(new[] { loop }, graph.GetInEdges(a));
        Assert.Equal(1, graph.GetOutDegree(a));
        Assert.Equal(1, graph.GetInDegree(a));
        Assert.Equal(new[] { loop }, graph.GetEdges(a, a));
    }

    // --- IReadableGraph: same concrete object, no adapter ---

    [Fact]
    public void Graph_Implements_IReadableGraph_AsRealMembers()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var ab = graph.AddEdge(a, b, new Dependency("ab"));

        // The same object flows into a consumer typed against the read surface — no adapter.
        IReadableGraph<Product, Dependency> read = graph;

        Assert.Equal(new[] { a, b }, read.Nodes);
        Assert.Equal(new[] { ab }, read.Edges);
        Assert.Equal(new Product("a"), read.GetNodePayload(a));
        Assert.Equal(new Dependency("ab"), read.GetEdgePayload(ab));
        Assert.Equal(a, read.GetSource(ab));
        Assert.Equal(b, read.GetTarget(ab));
        Assert.Equal(new[] { ab }, read.GetOutEdges(a));
        Assert.Equal(new[] { ab }, read.GetInEdges(b));
        Assert.Equal(new[] { ab }, read.GetEdges(a, b));
        Assert.Equal(1, read.GetOutDegree(a));
        Assert.Equal(1, read.GetInDegree(b));
    }

    // Static-dispatch usage: a consumer generic over the read surface compiles and runs.
    private static int CountReachableOut<TGraph>(TGraph g, NodeHandle from)
        where TGraph : IReadableGraph<Product, Dependency>
        => g.GetOutEdges(from).Count();

    [Fact]
    public void ReadSurface_IsUsableViaStaticDispatch()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Equal(2, CountReachableOut(graph, a));
    }

    // --- Reads-pure: reading never mutates observable state ---

    [Fact]
    public void Reads_AreRepeatable_AndDoNotMutate()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        // Reading many times yields identical results — no lazy build or memoization side effects.
        for (int i = 0; i < 3; i++)
        {
            Assert.Single(graph.GetOutEdges(a));
            Assert.Single(graph.GetInEdges(b));
            Assert.Equal(1, graph.GetOutDegree(a));
            Assert.Single(graph.GetEdges(a, b));
            Assert.Equal(2, graph.Nodes.Count());
            Assert.Single(graph.Edges);
        }
    }
}
