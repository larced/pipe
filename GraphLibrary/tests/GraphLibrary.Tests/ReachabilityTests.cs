using GraphLibrary;
using GraphLibrary.Traversal;

namespace GraphLibrary.Tests;

// Seam — the reachability roster, ticket 10 (#21): Reachable / Ancestors / IsReachable /
// ReachableFromMany over the layered GraphLibrary.Traversal surface (spec stories 31–32, 36
// depth-bounded, 37–38, 39, 86). Reverse (Ancestors) is first-class, not re-derived; results are
// eager, reusable node-set snapshots; walks take inline node/edge predicates and an optional depth
// bound; seed selection can be sourced from an opt-in SecondaryIndex. Every assertion drives only the
// public surface reachable from the Traversal namespace.
public class ReachabilityTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    // --- Reachable: the downstream node set (spec story 31) ---

    [Fact]
    public void Reachable_ReturnsEverythingDownstream_ExcludingStart()
    {
        // a -> b -> d, a -> c. Reachable(a) is the strict descendant set {b, c, d}; a itself is the
        // query origin, not a node reached by following an edge.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, c, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));

        Assert.Equal(new[] { b, c, d }, graph.Reachable(a).OrderBy(Name(graph)).ToArray());
        Assert.DoesNotContain(a, graph.Reachable(a));
    }

    [Fact]
    public void Reachable_LeafNode_IsEmpty()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Empty(graph.Reachable(b));
    }

    [Fact]
    public void Reachable_StartOnACycle_IncludesItselfBecauseAnEdgePathReturns()
    {
        // a -> b -> a: a is genuinely reachable from a by following edges, so it is in the set.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2"));

        Assert.Equal(new[] { a, b }, graph.Reachable(a).OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void Reachable_ParallelEdgesAndDiamond_YieldEachNodeOnce()
    {
        // a =2=> b, a -> c, b -> d, c -> d. The set dedups: {b, c, d}.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("1b"));
        graph.AddEdge(a, c, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));
        graph.AddEdge(c, d, new Dependency("4"));

        Assert.Equal(new[] { b, c, d }, graph.Reachable(a).OrderBy(Name(graph)).ToArray());
    }

    // --- Ancestors: reverse reachability is first-class (spec stories 32, 86) ---

    [Fact]
    public void Ancestors_ReturnsEverythingUpstream_ExcludingTheNode()
    {
        // a -> b -> d, c -> d. Ancestors(d) = {a, b, c}: "what depends on d", walking in-edges.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, d, new Dependency("2"));
        graph.AddEdge(c, d, new Dependency("3"));

        Assert.Equal(new[] { a, b, c }, graph.Ancestors(d).OrderBy(Name(graph)).ToArray());
        Assert.DoesNotContain(d, graph.Ancestors(d));
    }

    [Fact]
    public void Ancestors_IsTheReverseOfReachable()
    {
        // If y is reachable from x, then x is an ancestor of y — the two directions agree.
        var graph = new Graph<Product, Dependency>();
        var x = graph.AddNode(new Product("x"));
        var y = graph.AddNode(new Product("y"));
        graph.AddEdge(x, y, new Dependency("1"));

        Assert.Contains(y, graph.Reachable(x));
        Assert.Contains(x, graph.Ancestors(y));
    }

    // --- IsReachable: membership with early exit (spec story 86) ---

    [Fact]
    public void IsReachable_TrueWhenAnEdgePathExists_FalseOtherwise()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.True(graph.IsReachable(a, c));
        Assert.True(graph.IsReachable(a, b));
        Assert.False(graph.IsReachable(c, a)); // edges are directed
    }

    [Fact]
    public void IsReachable_SelfIsFalseWithoutACycle_TrueOnACycle()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.False(graph.IsReachable(a, a)); // no path of length >= 1 back to a

        graph.AddEdge(b, a, new Dependency("2"));
        Assert.True(graph.IsReachable(a, a)); // now the cycle returns to a
    }

    // --- ReachableFromMany: fan-in from several seeds (spec story 86) ---

    [Fact]
    public void ReachableFromMany_UnionsTheReachableSetsOfEverySeed()
    {
        // a -> x, b -> y. From {a, b} the downstream set is {x, y}.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var x = graph.AddNode(new Product("x"));
        var y = graph.AddNode(new Product("y"));
        graph.AddEdge(a, x, new Dependency("1"));
        graph.AddEdge(b, y, new Dependency("2"));

        Assert.Equal(new[] { x, y }, graph.ReachableFromMany(new[] { a, b }).OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void ReachableFromMany_EmptySeeds_IsEmpty()
    {
        var graph = new Graph<Product, Dependency>();
        graph.AddNode(new Product("a"));

        Assert.Empty(graph.ReachableFromMany(Array.Empty<NodeHandle>()));
    }

    [Fact]
    public void ReachableFromMany_IsTheUnionOfEachSeedsReachableSet()
    {
        // a -> b -> c, seeds {a, b}. The result is the union of Reachable(a)={b,c} and Reachable(b)={c}:
        // b is reached from a by a genuine edge, so it belongs to the set even though it is also a seed.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.Equal(new[] { b, c }, graph.ReachableFromMany(new[] { a, b }).OrderBy(Name(graph)).ToArray());
    }

    // --- Depth-bounded reachability (spec story 36) ---

    [Fact]
    public void Reachable_DepthBound_LimitsToNodesWithinNEdges()
    {
        // a -> b -> c -> d. Depth 1 = {b}; depth 2 = {b, c}; unbounded = {b, c, d}.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));
        graph.AddEdge(c, d, new Dependency("3"));

        Assert.Equal(new[] { b }, graph.Reachable(a, maxDepth: 1).ToArray());
        Assert.Equal(new[] { b, c }, graph.Reachable(a, maxDepth: 2).OrderBy(Name(graph)).ToArray());
        Assert.Equal(new[] { b, c, d }, graph.Reachable(a).OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void Reachable_DepthBound_TakesTheShortestDistanceToADiamondTip()
    {
        // a -> b -> d and a -> d. d sits at depth 1 via the direct edge, so depth 1 includes it.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, d, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));

        Assert.Equal(new[] { b, d }, graph.Reachable(a, maxDepth: 1).OrderBy(Name(graph)).ToArray());
    }

    [Fact]
    public void Reachable_DepthZero_IsEmpty()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Empty(graph.Reachable(a, maxDepth: 0));
    }

    [Fact]
    public void Reachable_NegativeDepth_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));

        Assert.Throws<ArgumentOutOfRangeException>(() => graph.Reachable(a, maxDepth: -1));
    }

    [Fact]
    public void Ancestors_DepthBound_LimitsUpstreamWalk()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.Equal(new[] { b }, graph.Ancestors(c, maxDepth: 1).ToArray());
    }

    // --- Inline predicate filtering (spec story 37) ---

    [Fact]
    public void Reachable_NodeFilter_ExcludesAndDoesNotWalkThroughFilteredNodes()
    {
        // a -> b -> c. Filtering out b hides b and cuts the path to c behind it.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.Empty(graph.Reachable(a, nodeFilter: p => p.Name != "b"));
    }

    [Fact]
    public void Reachable_NodeFilter_KeepsNodesReachableByAnAllowedRoute()
    {
        // a -> b -> d and a -> d. Filtering out b still leaves d reachable directly from a.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, d, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));

        Assert.Equal(new[] { d }, graph.Reachable(a, nodeFilter: p => p.Name != "b").ToArray());
    }

    [Fact]
    public void Reachable_NodeFilter_DoesNotConstrainTheSeedItself()
    {
        // The seed is the caller's chosen origin; a filter that would reject it still expands from it.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Equal(new[] { b }, graph.Reachable(a, nodeFilter: p => p.Name != "a").ToArray());
    }

    [Fact]
    public void Reachable_EdgeFilter_OnlyFollowsAllowedEdges()
    {
        // a =strong=> b, a =weak=> c. Following only "strong" edges reaches b, not c.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("strong"));
        graph.AddEdge(a, c, new Dependency("weak"));

        Assert.Equal(new[] { b }, graph.Reachable(a, edgeFilter: e => e.Kind == "strong").ToArray());
    }

    [Fact]
    public void Ancestors_EdgeFilter_AppliesToInEdges()
    {
        // a =strong=> c, b =weak=> c. Upstream of c over "strong" edges is just a.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, c, new Dependency("strong"));
        graph.AddEdge(b, c, new Dependency("weak"));

        Assert.Equal(new[] { a }, graph.Ancestors(c, edgeFilter: e => e.Kind == "strong").ToArray());
    }

    [Fact]
    public void IsReachable_RespectsFilters()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.False(graph.IsReachable(a, c, nodeFilter: p => p.Name != "b"));
        Assert.True(graph.IsReachable(a, c));
    }

    // --- Opt-in SecondaryIndex seed acceleration (spec story 38) ---

    [Fact]
    public void ReachableFromMany_SeedsFromASecondaryIndexLookup()
    {
        // Index products by name; look "seed"-tagged handles up by content and reach from them.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("seed"));
        var b = graph.AddNode(new Product("seed"));
        var x = graph.AddNode(new Product("x"));
        var y = graph.AddNode(new Product("y"));
        graph.AddEdge(a, x, new Dependency("1"));
        graph.AddEdge(b, y, new Dependency("2"));
        using var byName = graph.IndexNodesBy(p => p.Name);

        var reached = graph.ReachableFromMany(byName, "seed").OrderBy(Name(graph)).ToArray();

        Assert.Equal(new[] { x, y }, reached);
    }

    // --- Eager, reusable snapshot (spec story 39) ---

    [Fact]
    public void Reachable_IsAnEagerSnapshot_StableAcrossLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        var snapshot = graph.Reachable(a);
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, c, new Dependency("2"));

        Assert.Equal(new[] { b }, snapshot.ToArray()); // computed before c existed; unaffected
    }

    // --- Seed validation & argument guards ---

    [Fact]
    public void Reachable_StaleStart_ThrowsOnCall()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.RemoveNode(a);

        Assert.Throws<InvalidHandleException>(() => graph.Reachable(a));
    }

    [Fact]
    public void Ancestors_StaleNode_ThrowsOnCall()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.RemoveNode(a);

        Assert.Throws<InvalidHandleException>(() => graph.Ancestors(a));
    }

    [Fact]
    public void ReachableFromMany_StaleSeed_ThrowsOnCall()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.RemoveNode(b);

        Assert.Throws<InvalidHandleException>(() => graph.ReachableFromMany(new[] { a, b }));
    }

    [Fact]
    public void Reachable_NullGraph_Throws()
    {
        IReadableGraph<Product, Dependency> graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.Reachable(default));
    }

    [Fact]
    public void ReachableFromMany_NullSeeds_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        Assert.Throws<ArgumentNullException>(() => graph.ReachableFromMany((IEnumerable<NodeHandle>)null!));
    }

    // Orders a node set by payload name, so set-valued assertions are deterministic.
    private static Func<NodeHandle, string> Name(IReadableGraph<Product, Dependency> graph) =>
        n => graph.GetNodePayload(n).Name;
}
