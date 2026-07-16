using GraphLibrary;
using GraphLibrary.Traversal;

namespace GraphLibrary.Tests;

// Seam — the layered GraphLibrary.Traversal API, ticket 09 (#20): the pull-based Traverse primitive
// (lazy, LINQ-composable, visitor-steered, visited-set-terminating) plus the OutEdges/InEdges/
// OutNeighbors/InNeighbors incidence primitives (spec stories 28–30, 39–41). Every assertion drives
// only the public surface reachable from the Traversal namespace.
public class TraversalTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    // --- Incidence primitives: OutEdges / InEdges / OutNeighbors / InNeighbors ---

    [Fact]
    public void OutEdges_And_InEdges_ProjectIncidence()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var e = graph.AddEdge(a, b, new Dependency("dep"));

        Assert.Equal(new[] { e }, graph.OutEdges(a).ToArray());
        Assert.Empty(graph.OutEdges(b));
        Assert.Equal(new[] { e }, graph.InEdges(b).ToArray());
        Assert.Empty(graph.InEdges(a));
    }

    [Fact]
    public void OutNeighbors_And_InNeighbors_ProjectEndpoints()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, c, new Dependency("2"));

        Assert.Equal(new[] { b, c }, graph.OutNeighbors(a).ToArray());
        Assert.Equal(new[] { a }, graph.InNeighbors(b).ToArray());
        Assert.Equal(new[] { a }, graph.InNeighbors(c).ToArray());
    }

    [Fact]
    public void OutNeighbors_ParallelEdges_YieldTargetOncePerEdge()
    {
        // Edges are first-class, so the neighbor reached by two parallel edges appears twice — a
        // faithful projection of incidence; Distinct() collapses it if the caller wants each once.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Equal(new[] { b, b }, graph.OutNeighbors(a).ToArray());
        Assert.Equal(new[] { b }, graph.OutNeighbors(a).Distinct().ToArray());
    }

    [Fact]
    public void IncidencePrimitive_StaleHandle_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.RemoveNode(a);

        Assert.Throws<InvalidHandleException>(() => graph.OutEdges(a).ToArray());
        Assert.Throws<InvalidHandleException>(() => graph.InNeighbors(a).ToArray());
    }

    // --- Traverse: order, reachability, single-visit ---

    [Fact]
    public void Traverse_DepthFirst_VisitsPreOrderDownEachBranchFirst()
    {
        // a -> b -> d, a -> c. DFS exhausts the b branch before c.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, c, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));

        Assert.Equal(new[] { a, b, d, c }, graph.Traverse(a).ToArray());
    }

    [Fact]
    public void Traverse_BreadthFirst_VisitsByDistance()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, c, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));

        Assert.Equal(new[] { a, b, c, d }, graph.Traverse(a, TraversalOrder.BreadthFirst).ToArray());
    }

    [Fact]
    public void Traverse_OnlyReachableNodes_AreVisited()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddNode(new Product("island"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Equal(new[] { a, b }, graph.Traverse(a).ToArray());
    }

    // --- Termination through cycles, self-loops, parallel edges (visited-set) ---

    [Fact]
    public void Traverse_Cycle_TerminatesVisitingEachNodeOnce()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));
        graph.AddEdge(c, a, new Dependency("3")); // closes the cycle back to a

        Assert.Equal(new[] { a, b, c }, graph.Traverse(a).ToArray());
    }

    [Fact]
    public void Traverse_SelfLoop_TerminatesVisitingNodeOnce()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.AddEdge(a, a, new Dependency("self"));

        Assert.Equal(new[] { a }, graph.Traverse(a).ToArray());
    }

    [Fact]
    public void Traverse_ParallelEdges_VisitTargetOnce()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Equal(new[] { a, b }, graph.Traverse(a).ToArray());
    }

    // --- Visitor steering: Continue / SkipDescendants / Stop ---

    [Fact]
    public void Traverse_SkipDescendants_PrunesTheBranchButNotTheNode()
    {
        // a -> b -> (deep), a -> c. Skipping b's descendants drops deep but keeps b and c.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var deep = graph.AddNode(new Product("deep"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, c, new Dependency("2"));
        graph.AddEdge(b, deep, new Dependency("3"));

        var visited = graph.Traverse(a, node =>
            graph.GetNodePayload(node).Name == "b" ? VisitControl.SkipDescendants : VisitControl.Continue,
            TraversalOrder.BreadthFirst).ToArray();

        Assert.Equal(new[] { a, b, c }, visited);
        Assert.DoesNotContain(deep, visited);
    }

    [Fact]
    public void Traverse_SkipDescendants_NodeStillReachedByAnotherRouteIsVisited()
    {
        // a -> b -> d and a -> d. Pruning b's descendants must not hide d — a still reaches it directly.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var d = graph.AddNode(new Product("d"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, d, new Dependency("2"));
        graph.AddEdge(b, d, new Dependency("3"));

        var visited = graph.Traverse(a, node =>
            graph.GetNodePayload(node).Name == "b" ? VisitControl.SkipDescendants : VisitControl.Continue,
            TraversalOrder.BreadthFirst).ToArray();

        Assert.Contains(d, visited);
    }

    [Fact]
    public void Traverse_Stop_HaltsInclusiveOfTheTriggeringNode()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        var visited = graph.Traverse(a, node =>
            graph.GetNodePayload(node).Name == "b" ? VisitControl.Stop : VisitControl.Continue).ToArray();

        Assert.Equal(new[] { a, b }, visited); // b is produced, then the walk halts before c
    }

    // --- Laziness & LINQ-composability (spec story 28) ---

    [Fact]
    public void Traverse_IsLazy_StopsEarlyWithoutVisitingEverything()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        var visitCount = 0;
        var first = graph.Traverse(a, node => { visitCount++; return VisitControl.Continue; })
            .Take(2)
            .ToArray();

        Assert.Equal(new[] { a, b }, first);
        Assert.Equal(2, visitCount); // c was never pulled
    }

    [Fact]
    public void Traverse_IsLinqComposable()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        var names = graph.Traverse(a)
            .Select(n => graph.GetNodePayload(n).Name)
            .Where(name => name != "b")
            .ToArray();

        Assert.Equal(new[] { "a", "c" }, names);
    }

    // --- Materialized result is an eager, stable snapshot (spec story 39) ---

    [Fact]
    public void Traverse_ToList_IsAStableSnapshotAcrossLaterMutation()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        var snapshot = graph.Traverse(a).ToList(); // fully materialized here
        graph.AddNode(new Product("added-after"));

        Assert.Equal(new[] { a, b }, snapshot.ToArray());
    }

    // --- Fail-fast on mid-iteration structural mutation (spec story 40) ---

    [Fact]
    public void Traverse_AddNodeMidWalk_FailsFast()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.Traverse(a))
            {
                graph.AddNode(new Product("mutate"));
            }
        });
    }

    [Fact]
    public void Traverse_RemoveEdgeMidWalk_FailsFast()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var c = graph.AddNode(new Product("c"));
        var e = graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(b, c, new Dependency("2"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.Traverse(a, TraversalOrder.BreadthFirst))
            {
                graph.RemoveEdge(e);
            }
        });
    }

    [Fact]
    public void Traverse_SetPayloadMidWalk_DoesNotFailFast()
    {
        // SetPayload is structural-only-exempt (ticket 06): payload edits mid-walk are never rejected.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        var visited = 0;
        foreach (var node in graph.Traverse(a))
        {
            graph.SetPayload(node, new Product("renamed"));
            visited++;
        }

        Assert.Equal(2, visited);
    }

    // --- Seed validation & argument guards ---

    [Fact]
    public void Traverse_StaleStart_ThrowsOnCallNotDeferred()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.RemoveNode(a);

        // Eager: the throw happens on the Traverse call itself, before any MoveNext.
        Assert.Throws<InvalidHandleException>(() => graph.Traverse(a));
    }

    [Fact]
    public void Traverse_NullVisitor_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));

        Assert.Throws<ArgumentNullException>(() => graph.Traverse(a, (Func<NodeHandle, VisitControl>)null!));
    }
}
