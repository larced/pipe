using GraphLibrary;
using GraphLibrary.Traversal;

namespace GraphLibrary.Tests;

// Seam — dependency ordering, ticket 12 (#23): TopologicalSort over the layered GraphLibrary.Traversal
// surface (spec story 85). Every node precedes the nodes it points at; the sort is defined only for an
// acyclic graph and reports a cycle rather than returning a wrong order; results are eager snapshots.
public class OrderingTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    [Fact]
    public void TopologicalSort_OrdersEveryNodeBeforeTheNodesItPointsAt()
    {
        // a -> b -> d, a -> c -> d. Any valid order has a first and d last, with b and c between.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, c, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));
        graph.AddEdge(c, d, new Dependency("4"));

        var order = graph.TopologicalSort();

        Assert.Equal(4, order.Count);
        AssertPrecedes(order, a, b);
        AssertPrecedes(order, a, c);
        AssertPrecedes(order, b, d);
        AssertPrecedes(order, c, d);
    }

    [Fact]
    public void TopologicalSort_IsDeterministic_ReadyNodesInNodeEnumerationOrder()
    {
        // Two independent roots a and b (no edges between their subtrees) come out in the order the graph
        // enumerates them, so the sort is stable for a given graph.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var x = graph.AddNode(new Product("x"));
        graph.AddEdge(a, x, new Dependency("1"));
        graph.AddEdge(b, x, new Dependency("2"));

        Assert.Equal(new[] { a, b, x }, graph.TopologicalSort().ToArray());
    }

    [Fact]
    public void TopologicalSort_EmptyGraph_IsEmpty()
    {
        var graph = new Graph<Product, Dependency>();
        Assert.Empty(graph.TopologicalSort());
    }

    [Fact]
    public void TopologicalSort_IsolatedNodes_AllAppearOnce()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));

        Assert.Equal(new[] { a, b }, graph.TopologicalSort().OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void TopologicalSort_ParallelEdges_DoNotDuplicateNodes()
    {
        // Two a->b edges still put a before b, with each node emitted exactly once.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("1b"));

        Assert.Equal(new[] { a, b }, graph.TopologicalSort().ToArray());
    }

    [Fact]
    public void TopologicalSort_CyclicGraph_Throws()
    {
        // a -> b -> a: no dependency order exists.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));

        Assert.Throws<InvalidOperationException>(() => graph.TopologicalSort());
    }

    [Fact]
    public void TopologicalSort_SelfLoop_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.AddEdge(a, a, new Dependency("loop"));

        Assert.Throws<InvalidOperationException>(() => graph.TopologicalSort());
    }

    [Fact]
    public void TopologicalSort_IsAnEagerSnapshot_StableAcrossLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        var order = graph.TopologicalSort();
        graph.AddNode(new Product("c"));

        Assert.Equal(new[] { a, b }, order.ToArray()); // computed before c existed; unaffected
    }

    [Fact]
    public void TopologicalSort_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.TopologicalSort());
    }

    private static void AssertPrecedes(
        IReadOnlyList<NodeHandle> order, NodeHandle earlier, NodeHandle later) =>
        Assert.True(
            order.ToList().IndexOf(earlier) < order.ToList().IndexOf(later),
            "expected the first node to precede the second in the topological order");

    private static Func<NodeHandle, string> Name(IReadableGraph<Product, Dependency> graph) =>
        n => graph.GetNodePayload(n).Name;
}
