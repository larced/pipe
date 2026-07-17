using GraphLibrary;
using GraphLibrary.Rules;

namespace GraphLibrary.Tests;

// Seam 3 — Rule evaluation overlay, ticket 13 (#24): the Selection overlay (Selection, Instance,
// Region, InstanceId). This is a tracer-bullet vertical slice covering spec stories 45-49, 64-66:
// a mutable candidate configuration layered over the base Graph that never mutates it. The rules
// engine (Check / Availability / SelectionView) arrives in later tickets. Every assertion drives
// only the public API of the two layers (Graph and Rules), so the overlay's independence from the
// base graph is enforced by the suite, not just by convention (ADR 0005/0007).
public class SelectionOverlayTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    // Per-instance overlay data. Deliberately not a Product (TNode) — instance data carries no
    // base-graph payload (spec story 49). This models e.g. a chosen colour for one occurrence.
    private sealed record InstanceData(string Note);

    private static readonly Region FrontAxle = new("FrontAxle");
    private static readonly Region RearAxle = new("RearAxle");

    // --- Region: flat, opaque grouping label with equality, dynamically created, no registry ---

    [Fact]
    public void Region_EqualityIsByLabel()
    {
        Assert.Equal(new Region("FrontAxle"), new Region("FrontAxle"));
        Assert.NotEqual(new Region("FrontAxle"), new Region("RearAxle"));
        Assert.True(new Region("A") == new Region("A"));
        Assert.True(new Region("A") != new Region("B"));
        Assert.Equal(new Region("A").GetHashCode(), new Region("A").GetHashCode());
    }

    [Fact]
    public void Region_LabelIsCaseSensitive()
    {
        Assert.NotEqual(new Region("frontaxle"), new Region("FrontAxle"));
    }

    [Fact]
    public void Region_RejectsNullOrEmptyLabel()
    {
        // null throws the ArgumentException-derived ArgumentNullException; empty throws ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => new Region(null!));
        Assert.Throws<ArgumentException>(() => new Region(""));
    }

    [Fact]
    public void Region_ExposesItsLabel_SoHierarchyCanBeEncodedInTheValue()
    {
        // ADR 0007: hierarchy is an app convention encoded in the opaque value, not engine machinery.
        Assert.Equal("Vehicle/FrontAxle", new Region("Vehicle/FrontAxle").Label);
    }

    // --- Selection.Add: prototype handle + instance data + exactly one region -> instance id ---

    [Fact]
    public void NewSelection_IsEmpty()
    {
        var selection = new Selection<InstanceData>();

        Assert.Equal(0, selection.Count);
        Assert.Empty(selection.Instances);
        Assert.Empty(selection.ActiveRegions);
    }

    [Fact]
    public void Add_RecordsAnInstance_AndReturnsAnId()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var selection = new Selection<InstanceData>();

        var id = selection.Add(wheel, FrontAxle, new InstanceData("left"));

        Assert.Equal(1, selection.Count);
        Assert.True(selection.TryGet(id, out var instance));
        Assert.Equal(id, instance.Id);
        Assert.Equal(wheel, instance.Prototype);
        Assert.Equal(FrontAxle, instance.Region);
        Assert.Equal(new InstanceData("left"), instance.Data);
    }

    [Fact]
    public void Add_SamePrototypeTwice_ProducesDistinctOccurrences()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var selection = new Selection<InstanceData>();

        var first = selection.Add(wheel, FrontAxle, new InstanceData("left"));
        var second = selection.Add(wheel, FrontAxle, new InstanceData("right"));

        Assert.NotEqual(first, second);
        Assert.Equal(2, selection.Count);
        // Both occurrences reference the one prototype but are independently addressable.
        Assert.All(selection.Instances, i => Assert.Equal(wheel, i.Prototype));
    }

    [Fact]
    public void Add_RejectsAnUninitialisedRegion()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var selection = new Selection<InstanceData>();

        Assert.Throws<ArgumentException>(() => selection.Add(wheel, default, new InstanceData("x")));
    }

    // --- Selection.Remove: retract an instance by id (idempotent; cross-selection is misuse) ---

    [Fact]
    public void Remove_RetractsTheInstance_AndReportsWhetherItExisted()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var selection = new Selection<InstanceData>();
        var id = selection.Add(wheel, FrontAxle, new InstanceData("left"));

        Assert.True(selection.Remove(id));
        Assert.Equal(0, selection.Count);
        Assert.False(selection.TryGet(id, out _));

        // Idempotent: removing an already-gone id of this selection is a no-op false, not an error.
        Assert.False(selection.Remove(id));
    }

    [Fact]
    public void Remove_OfAnIdFromAnotherSelection_IsMisuseAndThrows()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var a = new Selection<InstanceData>();
        var b = new Selection<InstanceData>();
        var idFromA = a.Add(wheel, FrontAxle, new InstanceData("left"));

        // Mirrors the graph's cross-graph handle guard (ADR 0003 ethos): an id minted by another
        // selection is a programmer bug, not "already removed", so it fails fast rather than
        // silently aliasing an unrelated instance that happens to share the small integer id.
        Assert.Throws<InvalidInstanceIdException>(() => b.Remove(idFromA));
    }

    // --- Regions: exist iff carried, dynamically created, exactly one per instance ---

    [Fact]
    public void ActiveRegions_AreExactlyThoseCarriedByLiveInstances()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var selection = new Selection<InstanceData>();

        // No registry: a region springs into being on first use...
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, FrontAxle, new InstanceData("right"));
        var rearId = selection.Add(wheel, RearAxle, new InstanceData("spare"));

        Assert.Equal([FrontAxle, RearAxle], selection.ActiveRegions.OrderBy(r => r.Label));

        // ...and stops existing once the last instance carrying it is removed.
        selection.Remove(rearId);
        Assert.Equal([FrontAxle], selection.ActiveRegions);
    }

    // --- The load-bearing overlay guarantee: selection activity never mutates the base graph ---

    [Fact]
    public void BuildingASelection_NeverMutatesTheBaseGraph()
    {
        var graph = new Graph<Product, Dependency>();
        var engine = graph.AddNode(new Product("engine"));
        var wheel = graph.AddNode(new Product("wheel"));
        var edge = graph.AddEdge(engine, wheel, new Dependency("requires"));

        var nodesBefore = graph.Nodes.ToArray();
        var edgesBefore = graph.Edges.ToArray();

        var selection = new Selection<InstanceData>();
        var id = selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, RearAxle, new InstanceData("right"));
        selection.Add(engine, FrontAxle, new InstanceData("only"));
        selection.Remove(id);

        // The base graph is untouched: same nodes, same edges, same payloads (ADR 0005).
        Assert.Equal(nodesBefore, graph.Nodes);
        Assert.Equal(edgesBefore, graph.Edges);
        Assert.Equal(new Product("engine"), graph.GetNodePayload(engine));
        Assert.Equal(new Product("wheel"), graph.GetNodePayload(wheel));
        Assert.Equal(new Dependency("requires"), graph.GetEdgePayload(edge));
    }

    [Fact]
    public void Instances_IsAnEagerSnapshot_UnaffectedByLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));

        var snapshot = selection.Instances;
        selection.Add(wheel, RearAxle, new InstanceData("right"));

        Assert.Single(snapshot);
    }
}
