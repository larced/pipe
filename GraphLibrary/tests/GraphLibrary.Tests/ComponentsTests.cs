using GraphLibrary;
using GraphLibrary.Traversal;

namespace GraphLibrary.Tests;

// Seam — weakly-connected components, ticket 12 (#23): WeaklyConnectedComponents over the layered
// GraphLibrary.Traversal surface (spec story 87). Edges read as undirected; every node in exactly one
// component; isolated nodes are singletons; results are eager snapshots.
public class ComponentsTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    [Fact]
    public void WeaklyConnectedComponents_GroupsNodesConnectedIgnoringDirection()
    {
        // a -> b and c -> b: all three hang together weakly (b is a shared sink), one component.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(c, b, new Dependency("2"));

        var components = graph.WeaklyConnectedComponents();

        var only = Assert.Single(components);
        Assert.Equal(new[] { a, b, c }, only.OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void WeaklyConnectedComponents_SeparatesDisconnectedIslands()
    {
        // a -> b, and x -> y: two independent islands, two components.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var x = graph.AddNode(new Product("x"));
        var y = graph.AddNode(new Product("y"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(x, y, new Dependency("2"));

        var components = graph.WeaklyConnectedComponents();

        Assert.Equal(2, components.Count);
        Assert.Contains(components, comp => comp.SetEquals(new[] { a, b }));
        Assert.Contains(components, comp => comp.SetEquals(new[] { x, y }));
    }

    [Fact]
    public void WeaklyConnectedComponents_IsolatedNode_IsItsOwnSingleton()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        var lone = graph.AddNode(new Product("lone"));

        var components = graph.WeaklyConnectedComponents();

        Assert.Equal(2, components.Count);
        Assert.Contains(components, comp => comp.SetEquals(new[] { lone }));
    }

    [Fact]
    public void WeaklyConnectedComponents_ReachesAcrossReversedEdges()
    {
        // a -> b <- c -> d: a chain that only connects when direction is ignored — one component.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(c, b, new Dependency("2"));
        graph.AddEdge(c, d, new Dependency("3"));

        var only = Assert.Single(graph.WeaklyConnectedComponents());
        Assert.Equal(new[] { a, b, c, d }, only.OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void WeaklyConnectedComponents_EveryNodeAppearsInExactlyOneComponent()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));

        var components = graph.WeaklyConnectedComponents();

        var all = components.SelectMany(comp => comp).ToList();
        Assert.Equal(3, all.Count);            // no node dropped
        Assert.Equal(3, all.Distinct().Count()); // and none duplicated across components
        Assert.Contains(a, all);
        Assert.Contains(b, all);
        Assert.Contains(c, all);
    }

    [Fact]
    public void WeaklyConnectedComponents_SelfLoopsAndParallelEdges_ChangeNoGrouping()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, a, new Dependency("self"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("1b"));

        var only = Assert.Single(graph.WeaklyConnectedComponents());
        Assert.Equal(new[] { a, b }, only.OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void WeaklyConnectedComponents_EmptyGraph_IsEmpty()
    {
        var graph = new Graph<Product, Dependency>();
        Assert.Empty(graph.WeaklyConnectedComponents());
    }

    [Fact]
    public void WeaklyConnectedComponents_IsAnEagerSnapshot_StableAcrossLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        var components = graph.WeaklyConnectedComponents();
        graph.AddNode(new Product("c"));

        var only = Assert.Single(components); // computed before c existed; unaffected
        Assert.Equal(new[] { a, b }, only.OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void WeaklyConnectedComponents_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.WeaklyConnectedComponents());
    }

    private static Func<NodeHandle, string> Name(IReadableGraph<Product, Dependency> graph) =>
        n => graph.GetNodePayload(n).Name;
}
