using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 01 (#12): minimal multigraph, add & read.
// Every assertion drives only the public API; nothing here reaches into the slot-map
// backing, so a future representation change cannot break these tests (ADR 0002/0003).
public class GraphAddAndReadTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    [Fact]
    public void NewGraph_IsEmpty()
    {
        var graph = new Graph<Product, Dependency>();

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void AddNode_ReturnsHandle_AndPayloadReadsBack()
    {
        var graph = new Graph<Product, Dependency>();

        var handle = graph.AddNode(new Product("engine"));

        Assert.Equal(new Product("engine"), graph.GetNodePayload(handle));
        Assert.Single(graph.Nodes);
        Assert.Contains(handle, graph.Nodes);
    }

    [Fact]
    public void AddEdge_ReturnsHandle_AndEndpointsAndPayloadReadBack()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));

        var edge = graph.AddEdge(a, b, new Dependency("requires"));

        Assert.Equal(a, graph.GetSource(edge));
        Assert.Equal(b, graph.GetTarget(edge));
        Assert.Equal(new Dependency("requires"), graph.GetEdgePayload(edge));
        Assert.Single(graph.Edges);
        Assert.Contains(edge, graph.Edges);
    }

    [Fact]
    public void ParallelEdges_BetweenSameNodes_AreAcceptedAsDistinctEdges()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));

        var first = graph.AddEdge(a, b, new Dependency("requires"));
        var second = graph.AddEdge(a, b, new Dependency("suggests"));

        Assert.NotEqual(first, second);
        Assert.Equal(2, graph.Edges.Count());
        Assert.Equal(new Dependency("requires"), graph.GetEdgePayload(first));
        Assert.Equal(new Dependency("suggests"), graph.GetEdgePayload(second));
    }

    [Fact]
    public void SelfLoop_IsAcceptedByDefault()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));

        var loop = graph.AddEdge(a, a, new Dependency("self"));

        Assert.Equal(a, graph.GetSource(loop));
        Assert.Equal(a, graph.GetTarget(loop));
    }

    [Fact]
    public void HandleIdentity_IsIndependentOfPayload_DuplicatePayloadsDoNotCollide()
    {
        var graph = new Graph<Product, Dependency>();

        var first = graph.AddNode(new Product("same"));
        var second = graph.AddNode(new Product("same"));

        Assert.NotEqual(first, second);
        Assert.Equal(graph.GetNodePayload(first), graph.GetNodePayload(second));
    }

    [Fact]
    public void Handles_AreUsableAsDictionaryKeys()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("d"));

        var nodeLabels = new Dictionary<NodeHandle, string> { [a] = "a", [b] = "b" };
        var edgeLabels = new Dictionary<EdgeHandle, string> { [edge] = "d" };

        Assert.Equal("a", nodeLabels[a]);
        Assert.Equal("b", nodeLabels[b]);
        Assert.Equal("d", edgeLabels[edge]);
    }

    [Fact]
    public void Handles_AreValueTypesWithStructuralEquality()
    {
        Assert.True(typeof(NodeHandle).IsValueType);
        Assert.True(typeof(EdgeHandle).IsValueType);

        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));

        // The same node read back through Nodes equals the minted handle.
        var readBack = graph.Nodes.Single();
        Assert.Equal(a, readBack);
        Assert.Equal(a.GetHashCode(), readBack.GetHashCode());
    }
}
