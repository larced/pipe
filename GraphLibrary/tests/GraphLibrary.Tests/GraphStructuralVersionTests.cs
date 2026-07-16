using GraphLibrary;

namespace GraphLibrary.Tests;

// Seam 1 — Core Graph<TNode,TEdge>, ticket 06 (#17): the single monotonic structural-version
// counter and the caller-synchronized, best-effort fail-fast contract (spec stories 78–84).
// Every assertion drives only the public API. The contract under test: a structural mutation
// (node/edge add or remove) mid-enumeration makes that enumeration throw InvalidOperationException
// uniformly across the whole read surface, while an in-place SetPayload does NOT — it is
// structural-only, matching BCL indexer-set semantics.
public class GraphStructuralVersionTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);

    // --- AddNode / AddEdge / RemoveNode / RemoveEdge each invalidate a live enumeration ---

    [Fact]
    public void AddNode_DuringNodesEnumeration_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        graph.AddNode(new Product("a"));
        graph.AddNode(new Product("b"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.Nodes)
            {
                graph.AddNode(new Product("mutate"));
            }
        });
    }

    [Fact]
    public void AddEdge_DuringEdgesEnumeration_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.Edges)
            {
                graph.AddEdge(a, b, new Dependency("mutate"));
            }
        });
    }

    [Fact]
    public void RemoveNode_DuringNodesEnumeration_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.AddNode(new Product("b"));
        graph.AddNode(new Product("c"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.Nodes)
            {
                graph.RemoveNode(a);
            }
        });
    }

    [Fact]
    public void RemoveEdge_DuringOutEdgesEnumeration_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var e1 = graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.GetOutEdges(a))
            {
                graph.RemoveEdge(e1);
            }
        });
    }

    // --- Uniform: an unrelated structural change still invalidates an incidence enumeration ---

    [Fact]
    public void AddNode_DuringIncidenceEnumeration_Throws()
    {
        // The mutation touches nothing about a's out-edges, yet the incidence walk must still
        // fail fast — the counter is global, so "you forgot to synchronize" surfaces everywhere.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.GetOutEdges(a))
            {
                graph.AddNode(new Product("unrelated"));
            }
        });
    }

    [Fact]
    public void AddEdge_DuringInEdgesEnumeration_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.GetInEdges(b))
            {
                graph.AddEdge(a, b, new Dependency("mutate"));
            }
        });
    }

    [Fact]
    public void AddNode_DuringGetEdgesEnumeration_Throws()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in graph.GetEdges(a, b))
            {
                graph.AddNode(new Product("unrelated"));
            }
        });
    }

    // --- SetPayload is structural-only: it does NOT bump the counter, so enumeration survives ---

    [Fact]
    public void SetNodePayload_DuringNodesEnumeration_DoesNotThrow()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.AddNode(new Product("b"));

        var visited = 0;
        foreach (var _ in graph.Nodes)
        {
            graph.SetPayload(a, new Product("renamed"));
            visited++;
        }

        Assert.Equal(2, visited);
    }

    [Fact]
    public void SetEdgePayload_DuringOutEdgesEnumeration_DoesNotThrow()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        var e1 = graph.AddEdge(a, b, new Dependency("1"));
        graph.AddEdge(a, b, new Dependency("2"));

        var visited = 0;
        foreach (var _ in graph.GetOutEdges(a))
        {
            graph.SetPayload(e1, new Dependency("relabelled"));
            visited++;
        }

        Assert.Equal(2, visited);
    }

    // --- No-op removals are not structural changes: they must not bump the counter ---

    [Fact]
    public void NoOpRemoveNode_DuringEnumeration_DoesNotThrow()
    {
        // Removing an already-gone node changes no structure, so a concurrent enumeration is safe.
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        graph.AddNode(new Product("b"));
        graph.RemoveNode(a); // a is now stale

        var visited = 0;
        foreach (var _ in graph.Nodes)
        {
            Assert.False(graph.RemoveNode(a)); // idempotent no-op
            visited++;
        }

        Assert.Equal(1, visited); // only b remains
    }

    // --- The happy path: enumerating to completion with no mutation is unaffected ---

    [Fact]
    public void Enumeration_WithoutMutation_CompletesNormally()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));
        graph.AddEdge(a, b, new Dependency("1"));

        Assert.Equal(2, graph.Nodes.Count());
        Assert.Single(graph.Edges);
        Assert.Single(graph.GetOutEdges(a));
        Assert.Single(graph.GetInEdges(b));
        Assert.Single(graph.GetEdges(a, b));
    }

    // A snapshot taken before a mutation and materialized (ToList) before the next MoveNext is a
    // clean read; only crossing a structural mutation mid-walk trips the guard. Materializing first,
    // then mutating, then reading the list is always safe.
    [Fact]
    public void MaterializedSnapshot_TakenBeforeMutation_IsStable()
    {
        var graph = new Graph<Product, Dependency>();
        var a = graph.AddNode(new Product("a"));
        var b = graph.AddNode(new Product("b"));

        var snapshot = graph.Nodes.ToList();
        graph.AddNode(new Product("c"));

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(a, snapshot);
        Assert.Contains(b, snapshot);
    }
}
