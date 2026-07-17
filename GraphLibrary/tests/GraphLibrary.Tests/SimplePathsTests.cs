using GraphLibrary;
using GraphLibrary.Traversal;

namespace GraphLibrary.Tests;

// Seam — bounded simple-path enumeration, ticket 12 (#23): AllSimplePaths over the layered
// GraphLibrary.Traversal surface (spec story 36 depth-bounded). No node repeated; a mandatory hop bound;
// parallel edges yield distinct paths; results are eager snapshots.
public class SimplePathsTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    [Fact]
    public void AllSimplePaths_ReturnsEveryDistinctRouteWithinTheBound()
    {
        // a -> b -> d and a -> c -> d: two 2-hop simple routes from a to d.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, d, new Dependency("2"));
        graph.AddEdge(a, c, new Dependency("3"));
        graph.AddEdge(c, d, new Dependency("4"));

        var paths = graph.AllSimplePaths(a, d, maxLength: 5);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Equal(a, p.Start));
        Assert.All(paths, p => Assert.Equal(d, p.Target));
        Assert.Contains(paths, p => NodesOf(graph, p).SequenceEqual(new[] { a, b, d }));
        Assert.Contains(paths, p => NodesOf(graph, p).SequenceEqual(new[] { a, c, d }));
    }

    [Fact]
    public void AllSimplePaths_HonorsTheLengthBound()
    {
        // a -> d directly (1 hop) and a -> b -> d (2 hops). A bound of 1 admits only the direct route.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var d = graph.AddNode(new Product("d"));
        var ad = graph.AddEdge(a, d, new Dependency("direct"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, d, new Dependency("2"));

        var bounded = graph.AllSimplePaths(a, d, maxLength: 1);
        var single = Assert.Single(bounded);
        Assert.Equal(new[] { ad }, single.Edges.ToArray());

        Assert.Equal(2, graph.AllSimplePaths(a, d, maxLength: 2).Count); // both routes fit within 2 hops
    }

    [Fact]
    public void AllSimplePaths_NeverRepeatsANode_SoCyclesDoNotLoopForever()
    {
        // a -> b -> a cycle plus b -> c. The only simple route to c is a -> b -> c; the back edge b->a is
        // never followed onwards because a is already on the path.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));
        graph.AddEdge(b, c, new Dependency("3"));

        var path = Assert.Single(graph.AllSimplePaths(a, c, maxLength: 10));
        Assert.Equal(new[] { a, b, c }, NodesOf(graph, path).ToArray());
    }

    [Fact]
    public void AllSimplePaths_ParallelEdges_YieldDistinctPaths()
    {
        // Two edges from a to b are two distinct one-hop simple paths.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var e1 = graph.AddEdge(a, b, new Dependency("1"));
        var e2 = graph.AddEdge(a, b, new Dependency("2"));

        var paths = graph.AllSimplePaths(a, b, maxLength: 1);

        Assert.Equal(2, paths.Count);
        Assert.Contains(paths, p => p.Edges.Single() == e1);
        Assert.Contains(paths, p => p.Edges.Single() == e2);
    }

    [Fact]
    public void AllSimplePaths_NoRoute_IsEmpty()
    {
        // Edges are directed: c has no route to a.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, c, new Dependency("1"));

        Assert.Empty(graph.AllSimplePaths(c, a, maxLength: 5));
    }

    [Fact]
    public void AllSimplePaths_FromNodeToItself_IsTheTrivialZeroLengthPath()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));

        var path = Assert.Single(graph.AllSimplePaths(a, a, maxLength: 5));
        Assert.Empty(path.Edges);
        Assert.Equal(0, path.Length);
    }

    [Fact]
    public void AllSimplePaths_DepthZero_AdmitsOnlyTheTrivialPath()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Empty(graph.AllSimplePaths(a, b, maxLength: 0)); // no room for even one hop
        Assert.Single(graph.AllSimplePaths(a, a, maxLength: 0)); // the trivial path still fits
    }

    [Fact]
    public void AllSimplePaths_NegativeBound_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));

        Assert.Throws<ArgumentOutOfRangeException>(() => graph.AllSimplePaths(a, a, maxLength: -1));
    }

    [Fact]
    public void AllSimplePaths_StaleEndpoint_ThrowsOnCall()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.RemoveNode(b);

        Assert.Throws<InvalidHandleException>(() => graph.AllSimplePaths(a, b, maxLength: 3));
    }

    [Fact]
    public void AllSimplePaths_IsAnEagerSnapshot_StableAcrossLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        var paths = graph.AllSimplePaths(a, b, maxLength: 5);
        graph.AddEdge(a, b, new Dependency("2")); // a second route, added after the snapshot

        Assert.Single(paths); // computed before the parallel edge existed; unaffected
    }

    [Fact]
    public void AllSimplePaths_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.AllSimplePaths(default, default, 1));
    }

    // The node sequence a path passes through: its start, then the target of each edge in order.
    private static IEnumerable<NodeHandle> NodesOf(
        IReadableGraph<Product, Dependency> graph, Traversal.Path path)
    {
        yield return path.Start;
        foreach (EdgeHandle edge in path.Edges)
        {
            yield return graph.GetTarget(edge);
        }
    }
}
