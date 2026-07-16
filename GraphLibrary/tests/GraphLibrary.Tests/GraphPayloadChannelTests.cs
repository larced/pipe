using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 04 (#15): observable SetPayload change channel.
// Covers spec stories 6 & 10. Every assertion drives only the public API; nothing reaches into
// the slot-map backing, so a future representation change cannot break these tests (ADR 0002/0003).
public class GraphPayloadChannelTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    // --- Read/replace in place -------------------------------------------------------------

    [Fact]
    public void SetPayload_OnNode_ReplacesPayloadInPlace()
    {
        var graph = new Graph<Product, Dependency>();
        var handle = graph.AddNode(new Product("engine"));

        graph.SetPayload(handle, new Product("turbine"));

        Assert.Equal(new Product("turbine"), graph.GetNodePayload(handle));
    }

    [Fact]
    public void SetPayload_OnEdge_ReplacesPayloadInPlace()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("requires"));

        graph.SetPayload(edge, new Dependency("suggests"));

        Assert.Equal(new Dependency("suggests"), graph.GetEdgePayload(edge));
    }

    // --- Handle & structure are undisturbed (spec story 10) --------------------------------

    [Fact]
    public void SetPayload_DoesNotInvalidateHandle_SameHandleStillReadsAndEnumerates()
    {
        var graph = new Graph<Product, Dependency>();
        var handle = graph.AddNode(new Product("engine"));

        graph.SetPayload(handle, new Product("turbine"));

        // The very same handle value still resolves and still appears in Nodes.
        Assert.Equal(new Product("turbine"), graph.GetNodePayload(handle));
        Assert.Contains(handle, graph.Nodes);
        Assert.Equal(handle, graph.Nodes.Single());
    }

    [Fact]
    public void SetPayload_OnNode_LeavesGraphStructureUntouched()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("requires"));

        graph.SetPayload(a, new Product("a-renamed"));

        // Endpoints, incidence, and degree are exactly as before — mutation touched only payload.
        Assert.Equal(a, graph.GetSource(edge));
        Assert.Equal(b, graph.GetTarget(edge));
        Assert.Equal(edge, graph.GetOutEdges(a).Single());
        Assert.Equal(edge, graph.GetInEdges(b).Single());
        Assert.Equal(1, graph.GetOutDegree(a));
        Assert.Equal(1, graph.GetInDegree(b));
        Assert.Equal(new Dependency("requires"), graph.GetEdgePayload(edge));
    }

    [Fact]
    public void SetPayload_OnEdge_LeavesEndpointsAndIncidenceUntouched()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("requires"));

        graph.SetPayload(edge, new Dependency("suggests"));

        Assert.Equal(a, graph.GetSource(edge));
        Assert.Equal(b, graph.GetTarget(edge));
        Assert.Equal(edge, graph.GetOutEdges(a).Single());
        Assert.Equal(edge, graph.GetInEdges(b).Single());
        Assert.Equal(new Product("a"), graph.GetNodePayload(a));
        Assert.Equal(new Product("b"), graph.GetNodePayload(b));
    }

    // --- Observable channel (ADR 0002/0003) ------------------------------------------------

    [Fact]
    public void SetPayload_OnNode_BroadcastsChangeWithOldAndNewPayload()
    {
        var graph = new Graph<Product, Dependency>();
        var handle = graph.AddNode(new Product("engine"));

        var observed = new List<PayloadChange<NodeHandle, Product>>();
        graph.NodePayloadChanged += change => observed.Add(change);

        graph.SetPayload(handle, new Product("turbine"));

        var change = Assert.Single(observed);
        Assert.Equal(handle, change.Handle);
        Assert.Equal(new Product("engine"), change.OldPayload);
        Assert.Equal(new Product("turbine"), change.NewPayload);
    }

    [Fact]
    public void SetPayload_OnEdge_BroadcastsChangeWithOldAndNewPayload()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("requires"));

        var observed = new List<PayloadChange<EdgeHandle, Dependency>>();
        graph.EdgePayloadChanged += change => observed.Add(change);

        graph.SetPayload(edge, new Dependency("suggests"));

        var change = Assert.Single(observed);
        Assert.Equal(edge, change.Handle);
        Assert.Equal(new Dependency("requires"), change.OldPayload);
        Assert.Equal(new Dependency("suggests"), change.NewPayload);
    }

    [Fact]
    public void SetPayload_OnNode_DoesNotFireEdgeChannel_AndViceVersa()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var edge = graph.AddEdge(a, b, new Dependency("requires"));

        int nodeFires = 0, edgeFires = 0;
        graph.NodePayloadChanged += _ => nodeFires++;
        graph.EdgePayloadChanged += _ => edgeFires++;

        graph.SetPayload(a, new Product("a2"));
        Assert.Equal(1, nodeFires);
        Assert.Equal(0, edgeFires);

        graph.SetPayload(edge, new Dependency("suggests"));
        Assert.Equal(1, nodeFires);
        Assert.Equal(1, edgeFires);
    }

    [Fact]
    public void SetPayload_WithNoSubscribers_MutatesWithoutThrowing()
    {
        var graph = new Graph<Product, Dependency>();
        var handle = graph.AddNode(new Product("engine"));

        graph.SetPayload(handle, new Product("turbine"));

        Assert.Equal(new Product("turbine"), graph.GetNodePayload(handle));
    }

    // --- The channel is the only content-change signal; a secondary index consumes it ------

    [Fact]
    public void ChangeChannel_KeepsASecondaryIndexCorrectAcrossPayloadReplacement()
    {
        var graph = new Graph<Product, Dependency>();

        // A secondary index built entirely outside the core: name -> handle, maintained solely by
        // subscribing to the change channel. The core carries no built-in content index.
        var byName = new Dictionary<string, NodeHandle>();
        graph.NodePayloadChanged += change =>
        {
            byName.Remove(change.OldPayload.Name);
            byName[change.NewPayload.Name] = change.Handle;
        };

        var handle = graph.AddNode(new Product("engine"));
        byName[graph.GetNodePayload(handle).Name] = handle; // seed the initial key

        graph.SetPayload(handle, new Product("turbine"));

        // Old key is gone, new key resolves back to the same handle.
        Assert.False(byName.ContainsKey("engine"));
        Assert.Equal(handle, byName["turbine"]);
    }

    // --- Stale-handle posture: guard (throwing InvalidHandleException) is a later ticket ----

    [Fact]
    public void SetPayload_OnRemovedNode_IsANoOp_AndDoesNotBroadcast()
    {
        var graph = new Graph<Product, Dependency>();
        var handle = graph.AddNode(new Product("engine"));
        Assert.True(graph.RemoveNode(handle));

        int fires = 0;
        graph.NodePayloadChanged += _ => fires++;

        graph.SetPayload(handle, new Product("turbine"));

        Assert.Equal(0, fires);
        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void SetPayload_FromAnotherGraphsHandle_IsANoOp_AndDoesNotBroadcast()
    {
        var graph = new Graph<Product, Dependency>();
        var other = new Graph<Product, Dependency>();
        var foreign = other.AddNode(new Product("engine"));
        var local = graph.AddNode(new Product("local"));

        int fires = 0;
        graph.NodePayloadChanged += _ => fires++;

        graph.SetPayload(foreign, new Product("turbine"));

        Assert.Equal(0, fires);
        Assert.Equal(new Product("local"), graph.GetNodePayload(local));
    }
}
