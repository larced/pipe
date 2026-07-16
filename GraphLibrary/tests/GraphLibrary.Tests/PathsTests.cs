using GraphLibrary;
using GraphLibrary.Traversal;

namespace GraphLibrary.Tests;

// Seam — the paths + shortest-path slice, ticket 11 (#22): ShortestPath over the layered
// GraphLibrary.Traversal surface (spec stories 33–35). A Path is a sequence of EdgeHandles, unambiguous
// under parallel edges (33); BFS is the unweighted default (34); Dijkstra is opt-in for non-negative
// weights (35); negative weights are out of scope. Every assertion drives only the public surface
// reachable from the Traversal namespace.
public class PathsTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind, double Cost = 1d);

    // --- Route as an EdgeHandle sequence, unambiguous under parallel edges (spec story 33) ---

    [Fact]
    public void ShortestPath_ReturnsTheEdgeSequenceInRouteOrder()
    {
        // a -> b -> c. The route is [ab, bc]; endpoints and hop order line up.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var ab = graph.AddEdge(a, b, new Dependency("1"));
        var bc = graph.AddEdge(b, c, new Dependency("2"));

        var path = graph.ShortestPath(a, c);

        Assert.NotNull(path);
        Assert.Equal(a, path!.Start);
        Assert.Equal(c, path.Target);
        Assert.Equal(new[] { ab, bc }, path.Edges.ToArray());
        Assert.Equal(2, path.Length);
    }

    [Fact]
    public void ShortestPath_UnderParallelEdges_NamesTheSpecificEdgeTaken()
    {
        // a =two parallel edges=> b. The route names one concrete edge, not an ambiguous node pair.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var e1 = graph.AddEdge(a, b, new Dependency("1"));
        var e2 = graph.AddEdge(a, b, new Dependency("2"));

        var path = graph.ShortestPath(a, b);

        Assert.NotNull(path);
        var edge = Assert.Single(path!.Edges);
        Assert.True(edge == e1 || edge == e2);
    }

    [Fact]
    public void ShortestPath_NoRoute_IsNull()
    {
        // Edges are directed: c -> nothing, so a is unreachable from c.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, c, new Dependency("1"));

        Assert.Null(graph.ShortestPath(c, a));
    }

    [Fact]
    public void ShortestPath_FromNodeToItself_IsTheTrivialZeroLengthPath()
    {
        // A node is at distance 0 from itself — the trivial route, not null and not a cycle.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));

        var path = graph.ShortestPath(a, a);

        Assert.NotNull(path);
        Assert.Empty(path!.Edges);
        Assert.Equal(0, path.Length);
        Assert.Equal(a, path.Start);
        Assert.Equal(a, path.Target);
    }

    // --- BFS is the unweighted default: fewest edges (spec story 34) ---

    [Fact]
    public void ShortestPath_ByDefault_TakesTheFewestEdgesRoute()
    {
        // a -> b -> d (2 hops) and a -> d (1 hop). BFS prefers the single direct edge.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        var ad = graph.AddEdge(a, d, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));

        var path = graph.ShortestPath(a, d);

        Assert.NotNull(path);
        Assert.Equal(new[] { ad }, path!.Edges.ToArray());
    }

    [Fact]
    public void ShortestPath_TerminatesThroughCycles()
    {
        // a -> b -> a cycle, plus b -> c. The visited-set prevents looping; route to c is [ab, bc].
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var ab = graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));
        var bc = graph.AddEdge(b, c, new Dependency("3"));

        var path = graph.ShortestPath(a, c);

        Assert.Equal(new[] { ab, bc }, path!.Edges.ToArray());
    }

    // --- Dijkstra is opt-in for non-negative weights (spec story 35) ---

    [Fact]
    public void ShortestPath_Weighted_MinimisesTotalWeightNotHopCount()
    {
        // a -> c direct costs 10; a -> b -> c costs 1 + 1 = 2. Dijkstra prefers the cheaper 2-hop route,
        // where unweighted BFS would take the single direct edge.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var ab = graph.AddEdge(a, b, new Dependency("cheap", 1d));
        var bc = graph.AddEdge(b, c, new Dependency("cheap", 1d));
        var ac = graph.AddEdge(a, c, new Dependency("pricey", 10d));

        var weighted = graph.ShortestPath(a, c, e => e.Cost);
        var unweighted = graph.ShortestPath(a, c);

        Assert.Equal(new[] { ab, bc }, weighted!.Edges.ToArray());
        Assert.Equal(new[] { ac }, unweighted!.Edges.ToArray());
    }

    [Fact]
    public void ShortestPath_Weighted_UnderParallelEdges_PicksTheCheaperEdge()
    {
        // Two parallel a -> b edges, costs 5 and 2. Dijkstra takes the cheaper one specifically.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("dear", 5d));
        var cheap = graph.AddEdge(a, b, new Dependency("cheap", 2d));

        var path = graph.ShortestPath(a, b, e => e.Cost);

        Assert.Equal(new[] { cheap }, path!.Edges.ToArray());
    }

    [Fact]
    public void ShortestPath_Weighted_NoRoute_IsNull()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));

        Assert.Null(graph.ShortestPath(a, b, e => e.Cost));
    }

    [Fact]
    public void ShortestPath_Weighted_NegativeWeight_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("negative", -1d));

        Assert.Throws<ArgumentOutOfRangeException>(() => graph.ShortestPath(a, b, e => e.Cost));
    }

    // --- Eager, reusable snapshot (spec story 39) ---

    [Fact]
    public void ShortestPath_IsAnEagerSnapshot_StableAcrossLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var ab = graph.AddEdge(a, b, new Dependency("1"));

        var path = graph.ShortestPath(a, b);
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, c, new Dependency("2"));

        Assert.Equal(new[] { ab }, path!.Edges.ToArray()); // computed before c existed; unaffected
    }

    [Fact]
    public void ShortestPath_Edges_CannotBeCastBackToAMutableArray()
    {
        // The snapshot is genuinely immutable: the exposed sequence is not the backing EdgeHandle[].
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        var path = graph.ShortestPath(a, b);

        Assert.IsNotType<EdgeHandle[]>(path!.Edges);
    }

    // --- Endpoint validation & argument guards ---

    [Fact]
    public void ShortestPath_StaleFrom_ThrowsOnCall()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.RemoveNode(a);

        Assert.Throws<InvalidHandleException>(() => graph.ShortestPath(a, b));
    }

    [Fact]
    public void ShortestPath_StaleTo_ThrowsOnCall_EvenWhenUnreachable()
    {
        // `to` is validated up front, so a stale destination throws rather than quietly returning null.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.RemoveNode(b);

        Assert.Throws<InvalidHandleException>(() => graph.ShortestPath(a, b));
    }

    [Fact]
    public void ShortestPath_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.ShortestPath(default, default));
    }

    [Fact]
    public void ShortestPath_NullWeight_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        Assert.Throws<ArgumentNullException>(() => graph.ShortestPath(a, a, (Func<Dependency, double>)null!));
    }
}
