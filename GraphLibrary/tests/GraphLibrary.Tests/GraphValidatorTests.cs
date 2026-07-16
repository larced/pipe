using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 07 (#18): opt-in, off-by-default topology
// Validators (acyclicity, simple-graph, no-self-loops), enforced at mutation time and
// independently composable (spec stories 16–20). Every assertion drives only the public API;
// nothing here reaches into the slot-map backing (ADR 0001/0002/0003).
public class GraphValidatorTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    private static Graph<Product, Dependency> NewGraph() => new();

    // --- Off by default (story 15/19): the core accepts every topology when nothing is opted in ---

    [Fact]
    public void ByDefault_NoValidators_AreEnabled()
    {
        Assert.Equal(TopologyValidator.None, NewGraph().Validators);
    }

    [Fact]
    public void ByDefault_SelfLoops_ParallelEdges_AndCycles_AreAllAccepted()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));

        graph.AddEdge(a, a, new Dependency("self"));      // self-loop
        graph.AddEdge(a, b, new Dependency("1"));         // parallel pair…
        graph.AddEdge(a, b, new Dependency("2"));
        graph.AddEdge(b, a, new Dependency("back"));      // closes a cycle

        Assert.Equal(4, graph.Edges.Count());
    }

    // --- No-self-loops validator (story 18) ---

    [Fact]
    public void NoSelfLoops_RejectsSelfLoop_AtMutationTime()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        graph.AddValidator(TopologyValidator.NoSelfLoops);

        Assert.Throws<ValidatorRejectedException>(() => graph.AddEdge(a, a, new Dependency("self")));
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void NoSelfLoops_AcceptsEdgesBetweenDistinctNodes()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.NoSelfLoops);

        var edge = graph.AddEdge(a, b, new Dependency("d"));

        Assert.Contains(edge, graph.Edges);
    }

    [Fact]
    public void NoSelfLoops_DoesNotRejectParallelEdgesOrCycles()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.NoSelfLoops);

        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));   // parallel — allowed
        graph.AddEdge(b, a, new Dependency("back")); // cycle — allowed

        Assert.Equal(3, graph.Edges.Count());
    }

    // --- Simple-graph validator (story 17) ---

    [Fact]
    public void SimpleGraph_RejectsSecondEdgeBetweenSamePair()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.SimpleGraph);
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Throws<ValidatorRejectedException>(() => graph.AddEdge(a, b, new Dependency("2")));
        Assert.Single(graph.Edges);
    }

    [Fact]
    public void SimpleGraph_TreatsDirectionAsDistinct_AllowsReverseEdge()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.SimpleGraph);
        graph.AddEdge(a, b, new Dependency("forward"));

        // a→b and b→a are different ordered pairs — the reverse is not parallel.
        var back = graph.AddEdge(b, a, new Dependency("back"));

        Assert.Contains(back, graph.Edges);
    }

    [Fact]
    public void SimpleGraph_DoesNotRejectSelfLoopsThatAreNotParallel()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        graph.AddValidator(TopologyValidator.SimpleGraph);

        var loop = graph.AddEdge(a, a, new Dependency("self"));

        Assert.Contains(loop, graph.Edges);
    }

    [Fact]
    public void SimpleGraph_AllowsReAddingAnEdge_AfterTheFirstIsRemoved()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.SimpleGraph);
        var first = graph.AddEdge(a, b, new Dependency("1"));

        graph.RemoveEdge(first);
        var second = graph.AddEdge(a, b, new Dependency("2")); // pair is free again

        Assert.Contains(second, graph.Edges);
    }

    // --- Acyclicity validator (story 16) ---

    [Fact]
    public void Acyclic_RejectsAnEdgeThatWouldCloseACycle_AtMutationTime()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddValidator(TopologyValidator.Acyclic);
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        // c→a would close a→b→c→a.
        Assert.Throws<ValidatorRejectedException>(() => graph.AddEdge(c, a, new Dependency("close")));
        Assert.Equal(2, graph.Edges.Count());
    }

    [Fact]
    public void Acyclic_RejectsASelfLoop_AsACycleOfLengthOne()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        graph.AddValidator(TopologyValidator.Acyclic);

        Assert.Throws<ValidatorRejectedException>(() => graph.AddEdge(a, a, new Dependency("self")));
    }

    [Fact]
    public void Acyclic_AcceptsEdgesThatKeepTheGraphAcyclic()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddValidator(TopologyValidator.Acyclic);

        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));
        graph.AddEdge(a, c, new Dependency("shortcut")); // diamond, still acyclic

        Assert.Equal(3, graph.Edges.Count());
    }

    [Fact]
    public void Acyclic_AllowsParallelEdges_WhichNeverIntroduceACycleOnTheirOwn()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.Acyclic);

        graph.AddEdge(a, b, new Dependency("1"));
        var parallel = graph.AddEdge(a, b, new Dependency("2"));

        Assert.Contains(parallel, graph.Edges);
    }

    // --- Independent composability (story 19) ---

    [Fact]
    public void Validators_ComposeIndependently_EachRejectingOnlyItsOwnTopology()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.SimpleGraph | TopologyValidator.NoSelfLoops);

        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Throws<ValidatorRejectedException>(() => graph.AddEdge(a, a, new Dependency("self")));
        Assert.Throws<ValidatorRejectedException>(() => graph.AddEdge(a, b, new Dependency("2")));

        // A cycle is not opted into, so it is still accepted.
        var back = graph.AddEdge(b, a, new Dependency("back"));
        Assert.Contains(back, graph.Edges);
    }

    [Fact]
    public void AddValidator_IsCumulative_AndComposesAcrossCalls()
    {
        var graph = NewGraph();
        graph.AddValidator(TopologyValidator.Acyclic);
        graph.AddValidator(TopologyValidator.NoSelfLoops);

        Assert.Equal(TopologyValidator.Acyclic | TopologyValidator.NoSelfLoops, graph.Validators);
    }

    // --- Opt-in verifies the graph already satisfies the validator (honest invariant) ---

    [Fact]
    public void AddValidator_Acyclic_Throws_WhenGraphAlreadyContainsACycle()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, a, new Dependency("2")); // cycle present before opting in

        Assert.Throws<ValidatorRejectedException>(() => graph.AddValidator(TopologyValidator.Acyclic));
        Assert.Equal(TopologyValidator.None, graph.Validators);
    }

    [Fact]
    public void AddValidator_NoSelfLoops_Throws_WhenGraphAlreadyHasASelfLoop()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        graph.AddEdge(a, a, new Dependency("self"));

        Assert.Throws<ValidatorRejectedException>(() => graph.AddValidator(TopologyValidator.NoSelfLoops));
    }

    [Fact]
    public void AddValidator_SimpleGraph_Throws_WhenGraphAlreadyHasParallelEdges()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Throws<ValidatorRejectedException>(() => graph.AddValidator(TopologyValidator.SimpleGraph));
    }

    [Fact]
    public void AddValidator_Acyclic_Succeeds_OnAnAlreadyAcyclicGraph()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        graph.AddValidator(TopologyValidator.Acyclic);

        Assert.Equal(TopologyValidator.Acyclic, graph.Validators);
    }

    // --- A rejected mutation leaves the graph untouched (no partial edge, no incidence churn) ---

    [Fact]
    public void ARejectedEdge_LeavesDegreeAndIncidenceUnchanged()
    {
        var graph = NewGraph();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddValidator(TopologyValidator.Acyclic);
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Throws<ValidatorRejectedException>(() => graph.AddEdge(b, a, new Dependency("close")));

        Assert.Equal(1, graph.GetOutDegree(a));
        Assert.Equal(0, graph.GetInDegree(a));
        Assert.Equal(0, graph.GetOutDegree(b));
        Assert.Equal(1, graph.GetInDegree(b));
    }
}
