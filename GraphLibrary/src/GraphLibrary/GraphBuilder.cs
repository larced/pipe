namespace GraphLibrary;

/// <summary>
/// The non-generic entry point to the optional keyed fluent builder (ADR 0006). Coexists with the
/// generic core type <see cref="Graph{TNode,TEdge}"/> — the same "just a graph" hub — the way the BCL
/// pairs <c>Task</c> with <c>Task&lt;T&gt;</c>: a caller who never keys their payloads never touches
/// this class and constructs imperatively (spec story 25).
/// </summary>
public static class Graph
{
    /// <summary>
    /// Opens a declarative construction path for a small, well-defined graph whose payloads have a
    /// natural key: <paramref name="keySelector"/> derives a <typeparamref name="TKey"/> from each
    /// node payload so nodes and edges can be declared without threading handles, edges referencing
    /// their endpoints by key with deferred resolution (spec stories 21–24, ADR 0006). Terminate the
    /// chain with <see cref="GraphBuilder{TNode,TEdge,TKey}.Build()"/> to get back the same mutable
    /// <see cref="Graph{TNode,TEdge}"/>.
    /// </summary>
    public static GraphBuilder<TNode, TEdge, TKey> Build<TNode, TEdge, TKey>(Func<TNode, TKey> keySelector)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new GraphBuilder<TNode, TEdge, TKey>(keySelector);
    }
}

/// <summary>
/// A fluent, declarative builder for a keyed <see cref="Graph{TNode,TEdge}"/> — the small/well-defined
/// construction profile of ADR 0006, layered over the imperative core rather than replacing it. Nodes
/// are declared by payload (their key derived by the selector) and edges by endpoint <em>key</em>;
/// endpoints are resolved only at <see cref="Build()"/>, so an edge may name a node declared later
/// (forward references, spec story 22). Obtained from
/// <see cref="Graph.Build{TNode,TEdge,TKey}(System.Func{TNode,TKey})"/>.
/// </summary>
/// <remarks>
/// Deliberately a thin recorder: <see cref="AddNode"/> and <see cref="AddEdge"/> only stage the
/// declarations, and every check that needs the whole picture — duplicate keys, unresolved endpoint
/// keys — happens once in <see cref="Build()"/>. That is what lets deferred (forward) references work
/// and is why a duplicate key is caught at build time (spec story 24). The builder is single-use:
/// once built it should be discarded, and the returned graph is the live imperative core, mutable
/// exactly as before, with the unique keyed <see cref="SecondaryIndex{TKey}"/> still enforcing key
/// uniqueness across later mutation (spec story 23).
/// </remarks>
public sealed class GraphBuilder<TNode, TEdge, TKey>
    where TKey : notnull
{
    private readonly Func<TNode, TKey> _keySelector;

    // Declarations staged in order. Node payloads are materialised into the graph first (so keys are
    // all known before any endpoint is resolved); edges carry endpoint keys resolved against those.
    private readonly List<TNode> _nodePayloads = new();
    private readonly List<(TKey Source, TKey Target, TEdge Payload)> _edges = new();

    internal GraphBuilder(Func<TNode, TKey> keySelector) => _keySelector = keySelector;

    /// <summary>
    /// Stages a node carrying <paramref name="payload"/>; its key is derived by the selector at
    /// <see cref="Build()"/>. Returns this builder so declarations chain.
    /// </summary>
    public GraphBuilder<TNode, TEdge, TKey> AddNode(TNode payload)
    {
        _nodePayloads.Add(payload);
        return this;
    }

    /// <summary>
    /// Stages an edge from the node keyed <paramref name="source"/> to the node keyed
    /// <paramref name="target"/>, carrying <paramref name="payload"/>. The endpoint keys need not
    /// name nodes declared yet — they are resolved at <see cref="Build()"/> (forward references OK,
    /// spec story 22). Returns this builder so declarations chain.
    /// </summary>
    public GraphBuilder<TNode, TEdge, TKey> AddEdge(TKey source, TKey target, TEdge payload)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        _edges.Add((source, target, payload));
        return this;
    }

    /// <summary>
    /// Materialises the staged declarations into a fresh mutable <see cref="Graph{TNode,TEdge}"/> and
    /// returns it (spec story 23). A duplicate key across two node payloads throws
    /// <see cref="DuplicateKeyException"/>; an edge naming a key no node declares throws
    /// <see cref="KeyNotFoundException"/> — both at build time, so collisions and dangling references
    /// never survive into the returned graph.
    /// </summary>
    public Graph<TNode, TEdge> Build() => Build(out _);

    /// <summary>
    /// The <see cref="Build()"/> overload that also hands back the unique keyed
    /// <see cref="SecondaryIndex{TKey}"/> the builder was gated behind, so construction and later
    /// mutation share one identity/lookup mechanism (spec story 23, ADR 0006). The index stays
    /// attached to the graph's change channel and keeps key uniqueness enforced as payloads mutate;
    /// the parameterless <see cref="Build()"/> retains the very same enforced index internally.
    /// </summary>
    public Graph<TNode, TEdge> Build(out SecondaryIndex<TKey> index)
    {
        var graph = new Graph<TNode, TEdge>();

        // Materialise every node first, mapping key -> handle. A duplicate key is the collision story
        // 24 catches at build time; resolving it here (not in the index seed below) lets the message
        // name the offending key and keeps the graph from being half-populated past the clash.
        var handlesByKey = new Dictionary<TKey, NodeHandle>();
        foreach (TNode payload in _nodePayloads)
        {
            TKey key = _keySelector(payload);
            NodeHandle handle = graph.AddNode(payload);
            if (!handlesByKey.TryAdd(key, handle))
            {
                throw DuplicateKeyException.ForKey(key);
            }
        }

        // Now that every key is known, resolve each edge's endpoints — the point at which forward
        // references become legitimate (story 22). A key naming no declared node is a dangling
        // reference, thrown as a lookup miss.
        foreach ((TKey source, TKey target, TEdge payload) in _edges)
        {
            NodeHandle sourceHandle = Resolve(handlesByKey, source);
            NodeHandle targetHandle = Resolve(handlesByKey, target);
            graph.AddEdge(sourceHandle, targetHandle, payload);
        }

        // Hand back the same mutable graph with the unique keyed index still enforcing uniqueness
        // across later mutation (story 23). Seeding cannot collide — handlesByKey already proved the
        // keys distinct — so this never throws here.
        index = graph.IndexNodesBy(_keySelector, unique: true);
        return graph;
    }

    private static NodeHandle Resolve(Dictionary<TKey, NodeHandle> handlesByKey, TKey key)
    {
        if (!handlesByKey.TryGetValue(key, out NodeHandle handle))
        {
            throw new KeyNotFoundException(
                $"The edge references key '{key}', but no node with that key was declared.");
        }
        return handle;
    }
}
