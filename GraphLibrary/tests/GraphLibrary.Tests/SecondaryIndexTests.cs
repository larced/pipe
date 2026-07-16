using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 05 (#16): opt-in SecondaryIndex<TKey> content lookup.
// Covers spec stories 42-44. Every assertion drives only the public API; nothing reaches into the
// slot-map backing or the index internals, so a future representation change cannot break these
// tests (ADR 0002/0003). The index is a node content index (handles = NodeHandle): ADR 0003 fixes
// the single-parameter SecondaryIndex<TKey> name and keeps handles non-generic, and nodes are the
// content-lookup target the builder (stories 21-24) and seed selection (story 38) both need.
public class SecondaryIndexTests
{
    private sealed record Product(string Name, string Sku);

    // --- Story 42: opt-in key -> handle content lookup -------------------------------------

    [Fact]
    public void IndexNodesBy_LooksUpAHandleByItsPayloadKey()
    {
        var graph = new Graph<Product, string>();
        var engine = graph.AddNode(new Product("engine", "SKU-1"));
        graph.AddNode(new Product("wheel", "SKU-2"));

        var byName = graph.IndexNodesBy(p => p.Name);

        Assert.Equal(engine, Assert.Single(byName.Lookup("engine")));
    }

    [Fact]
    public void Lookup_ReturnsEveryHandleSharingAKey()
    {
        var graph = new Graph<Product, string>();
        // Two distinct nodes with duplicate content — identity is independent of payload (ADR 0003),
        // so both are legitimately present and both must come back under the shared key.
        var a = graph.AddNode(new Product("bolt", "SKU-A"));
        var b = graph.AddNode(new Product("bolt", "SKU-B"));

        var byName = graph.IndexNodesBy(p => p.Name);

        Assert.Equal(new[] { a, b }.OrderBy(h => h.GetHashCode()),
                     byName.Lookup("bolt").OrderBy(h => h.GetHashCode()));
    }

    [Fact]
    public void Lookup_OnUnknownKey_ReturnsEmpty()
    {
        var graph = new Graph<Product, string>();
        graph.AddNode(new Product("engine", "SKU-1"));

        var byName = graph.IndexNodesBy(p => p.Name);

        Assert.Empty(byName.Lookup("missing"));
    }

    [Fact]
    public void IndexNodesBy_IndexesNodesThatAlreadyExistAtCreation()
    {
        var graph = new Graph<Product, string>();
        var a = graph.AddNode(new Product("a", "SKU-A"));
        var b = graph.AddNode(new Product("b", "SKU-B"));

        var byName = graph.IndexNodesBy(p => p.Name);

        Assert.Equal(a, Assert.Single(byName.Lookup("a")));
        Assert.Equal(b, Assert.Single(byName.Lookup("b")));
    }

    // --- Story 43: kept correct across payload mutation via the change channel --------------

    [Fact]
    public void Index_ReKeysWhenPayloadMutationChangesTheKey()
    {
        var graph = new Graph<Product, string>();
        var handle = graph.AddNode(new Product("engine", "SKU-1"));
        var byName = graph.IndexNodesBy(p => p.Name);

        graph.SetPayload(handle, new Product("turbine", "SKU-1"));

        // Old key retired, new key resolves back to the very same (unchanged) handle.
        Assert.Empty(byName.Lookup("engine"));
        Assert.Equal(handle, Assert.Single(byName.Lookup("turbine")));
    }

    [Fact]
    public void Index_MutationThatLeavesTheKeyUnchanged_KeepsTheHandleFindable()
    {
        var graph = new Graph<Product, string>();
        var handle = graph.AddNode(new Product("engine", "SKU-1"));
        var byName = graph.IndexNodesBy(p => p.Name);

        // Key (Name) is unchanged; only the off-key Sku moved.
        graph.SetPayload(handle, new Product("engine", "SKU-99"));

        Assert.Equal(handle, Assert.Single(byName.Lookup("engine")));
    }

    [Fact]
    public void Index_MutatingOneOfTwoHandlesUnderAKey_LeavesTheOther()
    {
        var graph = new Graph<Product, string>();
        var a = graph.AddNode(new Product("bolt", "SKU-A"));
        var b = graph.AddNode(new Product("bolt", "SKU-B"));
        var byName = graph.IndexNodesBy(p => p.Name);

        graph.SetPayload(a, new Product("nut", "SKU-A"));

        Assert.Equal(a, Assert.Single(byName.Lookup("nut")));
        Assert.Equal(b, Assert.Single(byName.Lookup("bolt")));
    }

    [Fact]
    public void Index_SurvivesAChainOfReKeys()
    {
        var graph = new Graph<Product, string>();
        var handle = graph.AddNode(new Product("v1", "SKU-1"));
        var byName = graph.IndexNodesBy(p => p.Name);

        graph.SetPayload(handle, new Product("v2", "SKU-1"));
        graph.SetPayload(handle, new Product("v3", "SKU-1"));

        Assert.Empty(byName.Lookup("v1"));
        Assert.Empty(byName.Lookup("v2"));
        Assert.Equal(handle, Assert.Single(byName.Lookup("v3")));
    }

    [Fact]
    public void Index_CanBeKeyedOnAnyPayloadDerivedField()
    {
        var graph = new Graph<Product, string>();
        var handle = graph.AddNode(new Product("engine", "SKU-1"));
        var bySku = graph.IndexNodesBy(p => p.Sku);

        Assert.Equal(handle, Assert.Single(bySku.Lookup("SKU-1")));

        // A mutation that changes the Sku re-keys this Sku-index (and would not disturb a Name-index).
        graph.SetPayload(handle, new Product("engine", "SKU-2"));

        Assert.Empty(bySku.Lookup("SKU-1"));
        Assert.Equal(handle, Assert.Single(bySku.Lookup("SKU-2")));
    }

    // --- Story 44: the core carries no built-in content index; indexing is opt-in ----------

    [Fact]
    public void AGraphThatOptsOut_MutatesPayloadsFreelyWithNoIndexInvolved()
    {
        var graph = new Graph<Product, string>();
        var handle = graph.AddNode(new Product("engine", "SKU-1"));

        // No index created: payload mutation still works and the core exposes no content lookup of
        // its own (there is no Graph.Lookup / Graph member keyed by payload — enforced at compile time).
        graph.SetPayload(handle, new Product("turbine", "SKU-1"));

        Assert.Equal(new Product("turbine", "SKU-1"), graph.GetNodePayload(handle));
    }

    [Fact]
    public void TwoIndependentIndexesOverTheSameGraph_AreBothMaintained()
    {
        var graph = new Graph<Product, string>();
        var handle = graph.AddNode(new Product("engine", "SKU-1"));
        var byName = graph.IndexNodesBy(p => p.Name);
        var bySku = graph.IndexNodesBy(p => p.Sku);

        graph.SetPayload(handle, new Product("turbine", "SKU-2"));

        Assert.Equal(handle, Assert.Single(byName.Lookup("turbine")));
        Assert.Equal(handle, Assert.Single(bySku.Lookup("SKU-2")));
    }

    [Fact]
    public void Dispose_DetachesTheIndex_SoLaterMutationsAreNotTracked()
    {
        var graph = new Graph<Product, string>();
        var handle = graph.AddNode(new Product("engine", "SKU-1"));
        var byName = graph.IndexNodesBy(p => p.Name);

        byName.Dispose();
        graph.SetPayload(handle, new Product("turbine", "SKU-1"));

        // After detaching, the channel no longer updates the index: it is frozen at its last state,
        // proving the index is an external opt-in subscriber, not part of the core (story 44).
        Assert.Equal(handle, Assert.Single(byName.Lookup("engine")));
        Assert.Empty(byName.Lookup("turbine"));
    }

    // --- Boundary: the payload channel signals content changes, not structural add/remove ---

    [Fact]
    public void Index_ReflectsTheNodeSetAtCreation_StructuralChangesAreNotTrackedByThisChannel()
    {
        var graph = new Graph<Product, string>();
        var seeded = graph.AddNode(new Product("seeded", "SKU-1"));
        var byName = graph.IndexNodesBy(p => p.Name);

        // The payload change channel (ticket 04) is the only signal this index consumes, and it fires
        // on SetPayload only. A node added after creation is therefore not seen; a node removed after
        // creation leaves a stale handle whose use fails fast under the ADR 0003 guard. This pins the
        // intended boundary — structural tracking would need a separate channel, out of scope here.
        var addedLater = graph.AddNode(new Product("later", "SKU-2"));

        Assert.Equal(seeded, Assert.Single(byName.Lookup("seeded")));
        Assert.Empty(byName.Lookup("later"));
        Assert.NotEqual(default, addedLater);
    }
}
