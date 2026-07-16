using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 08 (#19): the optional keyed fluent builder
// (Graph.Build<TNode,TEdge,TKey>) over the imperative core, gated behind a unique keyed
// SecondaryIndex (spec stories 21–25, ADR 0006). Every assertion drives only the public API;
// nothing here reaches into the slot-map backing or the builder internals, so a future
// representation change cannot break these tests (ADR 0002/0003).
public class GraphKeyedBuilderTests
{
    private sealed record Product(string Sku, string Name);
    private sealed record Dependency(string Kind);

    // --- Story 21: declarative construction without threading handles -----------------------

    [Fact]
    public void Build_ConstructsAGraphDeclaratively_DerivingEachKeyFromItsPayload()
    {
        Graph<Product, Dependency> graph = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddNode(new Product("SKU-1", "engine"))
            .AddNode(new Product("SKU-2", "wheel"))
            .Build();

        // Two nodes present, and the same object is the mutable Graph the imperative core hands back.
        Assert.Equal(2, graph.Nodes.Count());
        Assert.IsType<Graph<Product, Dependency>>(graph);
    }

    // --- Story 22: edges referenced by key, with deferred (forward-reference) resolution -----

    [Fact]
    public void Build_ResolvesEdgeEndpointsByKey()
    {
        Graph<Product, Dependency> graph = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddNode(new Product("SKU-1", "engine"))
            .AddNode(new Product("SKU-2", "wheel"))
            .AddEdge("SKU-1", "SKU-2", new Dependency("needs"))
            .Build();

        var index = graph.IndexNodesBy(p => p.Sku);
        NodeHandle source = Assert.Single(index.Lookup("SKU-1"));
        NodeHandle target = Assert.Single(index.Lookup("SKU-2"));

        EdgeHandle edge = Assert.Single(graph.GetEdges(source, target));
        Assert.Equal(new Dependency("needs"), graph.GetEdgePayload(edge));
    }

    [Fact]
    public void Build_AllowsForwardReferences_AnEdgeMayNameANodeDefinedLater()
    {
        // The edge is declared before its target node exists. Deferred resolution — all nodes are
        // materialised before any endpoint is resolved — makes the forward reference legitimate.
        Graph<Product, Dependency> graph = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddEdge("SKU-1", "SKU-2", new Dependency("needs"))
            .AddNode(new Product("SKU-1", "engine"))
            .AddNode(new Product("SKU-2", "wheel"))
            .Build();

        Assert.Single(graph.Edges);
    }

    [Fact]
    public void Build_WhenAnEdgeNamesAnUnknownKey_Throws()
    {
        GraphBuilder<Product, Dependency, string> builder = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddNode(new Product("SKU-1", "engine"))
            .AddEdge("SKU-1", "SKU-MISSING", new Dependency("needs"));

        var ex = Assert.Throws<KeyNotFoundException>(() => builder.Build());
        Assert.Contains("SKU-MISSING", ex.Message);
    }

    // --- Story 23: .Build() hands back the SAME mutable Graph, index still enforced ----------

    [Fact]
    public void Build_ReturnsTheSameMutableGraph_ThatAcceptsFurtherImperativeMutation()
    {
        Graph<Product, Dependency> graph = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddNode(new Product("SKU-1", "engine"))
            .Build();

        // The returned graph is the live imperative core: an ordinary AddNode still works on it.
        NodeHandle added = graph.AddNode(new Product("SKU-2", "wheel"));
        Assert.Equal(new Product("SKU-2", "wheel"), graph.GetNodePayload(added));
        Assert.Equal(2, graph.Nodes.Count());
    }

    [Fact]
    public void Build_KeepsTheUniqueKeyedIndexEnforced_ForLaterMutation()
    {
        NodeHandle two = default;
        Graph<Product, Dependency> graph = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddNode(new Product("SKU-1", "engine"))
            .AddNode(new Product("SKU-2", "wheel"))
            .Build(out SecondaryIndex<string> index);

        // The index survives Build and is a live content lookup over the constructed graph.
        two = Assert.Single(index.Lookup("SKU-2"));
        Assert.Equal("wheel", graph.GetNodePayload(two).Name);

        // Uniqueness stays enforced across later mutation: re-keying SKU-2's payload onto SKU-1's
        // key would collide, and the still-attached unique index rejects it.
        Assert.Throws<DuplicateKeyException>(
            () => graph.SetPayload(two, new Product("SKU-1", "wheel")));

        // The rejected mutation left the graph exactly as it was — the veto rolled the payload back,
        // so the node still carries SKU-2 and the index still resolves it under SKU-2 (no half-apply).
        Assert.Equal("SKU-2", graph.GetNodePayload(two).Sku);
        Assert.Equal(two, Assert.Single(index.Lookup("SKU-2")));
    }

    [Fact]
    public void Build_UniqueIndexReKeysCleanly_WhenTheNewKeyDoesNotCollide()
    {
        Graph<Product, Dependency> graph = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddNode(new Product("SKU-1", "engine"))
            .Build(out SecondaryIndex<string> index);

        NodeHandle handle = Assert.Single(index.Lookup("SKU-1"));
        graph.SetPayload(handle, new Product("SKU-9", "engine"));

        Assert.Empty(index.Lookup("SKU-1"));
        Assert.Equal(handle, Assert.Single(index.Lookup("SKU-9")));
    }

    // --- Story 24: a duplicate key during keyed construction throws at build time -----------

    [Fact]
    public void Build_WhenTwoPayloadsShareAKey_ThrowsAtBuildTime()
    {
        GraphBuilder<Product, Dependency, string> builder = Graph
            .Build<Product, Dependency, string>(p => p.Sku)
            .AddNode(new Product("SKU-1", "engine"))
            .AddNode(new Product("SKU-1", "turbine"));   // same Sku, distinct payload

        var ex = Assert.Throws<DuplicateKeyException>(() => builder.Build());
        Assert.Contains("SKU-1", ex.Message);
    }

    // --- Story 25: keyless payloads use the imperative path, unforced into a key ------------

    [Fact]
    public void KeylessGraphs_UseTheImperativePathWithNoKey()
    {
        // No Build, no keySelector: a plain graph whose payloads (here, ints with no natural key)
        // are added and connected purely by handle. This is the path story 25 preserves.
        var graph = new Graph<int, int>();
        NodeHandle a = graph.AddNode(1);
        NodeHandle b = graph.AddNode(1);   // duplicate payload, no key, no collision
        graph.AddEdge(a, b, 42);

        Assert.Equal(2, graph.Nodes.Count());
        Assert.Single(graph.Edges);
    }
}
