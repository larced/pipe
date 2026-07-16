using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 14 (#14): opaque handle guards. A handle carries an
// internal id + generation/graph stamp, so a handle from another graph or one whose element was
// removed fails fast with InvalidHandleException instead of silently reading the wrong element or
// returning garbage (ADR 0003; spec stories 11–14). Every assertion drives only the public API.
public class GraphHandleGuardTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    private static Graph<Product, Dependency> NewGraph() => new();

    // --- Cross-graph: a handle minted by one graph, used against another, throws ---

    [Fact]
    public void GetNodePayload_WithHandleFromAnotherGraph_Throws()
    {
        var graphA = NewGraph();
        var graphB = NewGraph();
        var foreign = graphA.AddNode(new Product("a"));
        graphB.AddNode(new Product("b"));   // graphB has a live slot 0 the foreign handle would alias

        Assert.Throws<InvalidHandleException>(() => graphB.GetNodePayload(foreign));
    }

    [Fact]
    public void GetEdgePayload_WithHandleFromAnotherGraph_Throws()
    {
        var graphA = NewGraph();
        var graphB = NewGraph();
        var a = graphA.AddNode(new Product("a"));
        var b = graphA.AddNode(new Product("b"));
        var foreign = graphA.AddEdge(a, b, new Dependency("d"));

        Assert.Throws<InvalidHandleException>(() => graphB.GetEdgePayload(foreign));
    }

    [Fact]
    public void EndpointReads_WithHandleFromAnotherGraph_Throw()
    {
        var graphA = NewGraph();
        var graphB = NewGraph();
        var a = graphA.AddNode(new Product("a"));
        var b = graphA.AddNode(new Product("b"));
        var foreign = graphA.AddEdge(a, b, new Dependency("d"));

        Assert.Throws<InvalidHandleException>(() => graphB.GetSource(foreign));
        Assert.Throws<InvalidHandleException>(() => graphB.GetTarget(foreign));
    }

    [Fact]
    public void IncidenceAndDegree_WithHandleFromAnotherGraph_Throw()
    {
        var graphA = NewGraph();
        var graphB = NewGraph();
        var foreign = graphA.AddNode(new Product("a"));

        Assert.Throws<InvalidHandleException>(() => graphB.GetOutEdges(foreign));
        Assert.Throws<InvalidHandleException>(() => graphB.GetInEdges(foreign));
        Assert.Throws<InvalidHandleException>(() => graphB.GetOutDegree(foreign));
        Assert.Throws<InvalidHandleException>(() => graphB.GetInDegree(foreign));
    }

    [Fact]
    public void RemoveNode_WithHandleFromAnotherGraph_Throws()
    {
        var graphA = NewGraph();
        var graphB = NewGraph();
        var foreign = graphA.AddNode(new Product("a"));

        // Cross-graph misuse is a programming error, not idempotent "already removed".
        Assert.Throws<InvalidHandleException>(() => graphB.RemoveNode(foreign));
    }

    [Fact]
    public void RemoveEdge_WithHandleFromAnotherGraph_Throws()
    {
        var graphA = NewGraph();
        var graphB = NewGraph();
        var a = graphA.AddNode(new Product("a"));
        var b = graphA.AddNode(new Product("b"));
        var foreign = graphA.AddEdge(a, b, new Dependency("d"));

        Assert.Throws<InvalidHandleException>(() => graphB.RemoveEdge(foreign));
    }

    [Fact]
    public void DefaultHandle_IsRejected_AsNotBelongingToTheGraph()
    {
        var graph = NewGraph();

        Assert.Throws<InvalidHandleException>(() => graph.GetNodePayload(default));
        Assert.Throws<InvalidHandleException>(() => graph.GetEdgePayload(default));
    }

    // --- Stale: the element was already removed, so reads throw rather than return garbage ---

    [Fact]
    public void GetNodePayload_WithStaleHandle_Throws()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        graph.RemoveNode(a);

        Assert.Throws<InvalidHandleException>(() => graph.GetNodePayload(a));
    }

    [Fact]
    public void GetEdgePayload_WithStaleHandle_Throws()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));
        graph.RemoveEdge(edge);

        Assert.Throws<InvalidHandleException>(() => graph.GetEdgePayload(edge));
    }

    [Fact]
    public void EndpointReads_WithStaleEdgeHandle_Throw()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));
        graph.RemoveEdge(edge);

        Assert.Throws<InvalidHandleException>(() => graph.GetSource(edge));
        Assert.Throws<InvalidHandleException>(() => graph.GetTarget(edge));
    }

    [Fact]
    public void IncidenceAndDegree_WithStaleNodeHandle_Throw()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        graph.RemoveNode(a);

        // Eager validation: the throw fires on the call, not on deferred enumeration.
        Assert.Throws<InvalidHandleException>(() => graph.GetOutEdges(a));
        Assert.Throws<InvalidHandleException>(() => graph.GetInEdges(a));
        Assert.Throws<InvalidHandleException>(() => graph.GetOutDegree(a));
        Assert.Throws<InvalidHandleException>(() => graph.GetInDegree(a));
    }

    [Fact]
    public void GetEdges_WithStaleEndpoint_Throws()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.RemoveNode(a);

        Assert.Throws<InvalidHandleException>(() => graph.GetEdges(a, b));
    }

    [Fact]
    public void EdgeHandle_GoesStale_WhenItsEndpointNodeIsRemoved()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));

        graph.RemoveNode(a);   // sweeps `edge` as incident, so the edge handle is now stale

        Assert.Throws<InvalidHandleException>(() => graph.GetEdgePayload(edge));
    }

    // --- Stale after slot reuse: the reused slot must not resurrect the old handle ---

    [Fact]
    public void StaleHandle_DoesNotAliasNewElementReusingItsSlot()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        graph.RemoveNode(a);
        var reused = graph.AddNode(new Product("reused"));   // reuses a's freed slot, bumped generation

        // The old handle stays invalid even though its slot index is live again...
        Assert.Throws<InvalidHandleException>(() => graph.GetNodePayload(a));
        // ...and the two handles are distinct identities despite sharing a slot index.
        Assert.NotEqual(a, reused);
        Assert.Equal(new Product("reused"), graph.GetNodePayload(reused));
    }

    [Fact]
    public void AddEdge_WithStaleEndpointHandle_Throws()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.RemoveNode(a);

        Assert.Throws<InvalidHandleException>(() => graph.AddEdge(a, b, new Dependency("d")));
    }

    [Fact]
    public void AddEdge_WithEndpointFromAnotherGraph_Throws()
    {
        var graphA = NewGraph();
        var graphB = NewGraph();
        var foreign = graphA.AddNode(new Product("a"));
        var local = graphB.AddNode(new Product("b"));

        Assert.Throws<InvalidHandleException>(() => graphB.AddEdge(foreign, local, new Dependency("d")));
        Assert.Throws<InvalidHandleException>(() => graphB.AddEdge(local, foreign, new Dependency("d")));
    }

    // --- Idempotent removal is preserved: a stale same-graph handle still returns false ---

    [Fact]
    public void RemoveNode_WithStaleSameGraphHandle_ReturnsFalse_NotThrows()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));

        Assert.True(graph.RemoveNode(a));
        Assert.False(graph.RemoveNode(a));   // stale, same graph → idempotent, not an exception
    }

    [Fact]
    public void RemoveEdge_WithStaleSameGraphHandle_ReturnsFalse_NotThrows()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));

        Assert.True(graph.RemoveEdge(edge));
        Assert.False(graph.RemoveEdge(edge));
    }

    // --- Handle identity is completely independent of payload ---

    [Fact]
    public void DuplicatePayloads_YieldDistinctHandleIdentities()
    {
        var graph = NewGraph();
        var first = graph.AddNode(new Product("same"));
        var second = graph.AddNode(new Product("same"));

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(graph.GetNodePayload(first), graph.GetNodePayload(second));
    }

    [Fact]
    public void EdgeHandleIdentity_IsIndependentOfDuplicatePayloads()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var first = graph.AddEdge(a, b, new Dependency("same"));
        var second = graph.AddEdge(a, b, new Dependency("same"));

        Assert.NotEqual(first, second);
        Assert.Equal(graph.GetEdgePayload(first), graph.GetEdgePayload(second));
    }
}
