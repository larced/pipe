namespace GraphLibrary;

/// <summary>
/// An opt-in content index mapping a payload-derived <typeparamref name="TKey"/> back to the
/// <see cref="NodeHandle"/>s whose payload yields that key (CONTEXT.md → Secondary index, ADR 0003):
/// the reverse of the graph's own payload→nothing direction, letting a consumer look a node up
/// <em>by content</em> when identity alone won't do. Created through
/// <see cref="Graph{TNode,TEdge}.IndexNodesBy{TKey}(System.Func{TNode,TKey})"/> and kept correct
/// across payload mutation by subscribing to the graph's <see cref="Graph{TNode,TEdge}.NodePayloadChanged"/>
/// channel — so content lookups never go stale (spec story 43).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately single-parameter in <typeparamref name="TKey"/> and <b>not</b> part of core identity
/// (ADR 0003): handles stay non-generic and the graph carries no built-in content index, so a graph
/// you never index stays lean and domain-agnostic (spec story 44). Because identity is independent of
/// payload, several distinct nodes may share a key; <see cref="Lookup"/> therefore returns a
/// collection, not a single handle.
/// </para>
/// <para>
/// The index snapshots the nodes present when it is created and then tracks <em>payload</em> changes
/// through the change channel. That channel is its only signal, and it fires on
/// <c>SetPayload</c> alone (ticket 04): structural add/remove after creation is not tracked here — a
/// later node is not seen, and a removed node's handle is retired only when its slot is re-keyed, its
/// stale use otherwise failing fast under the ADR 0003 guard. <see cref="Dispose"/> detaches the index
/// from the channel; a detached index is frozen and costs the graph nothing.
/// </para>
/// </remarks>
public sealed class SecondaryIndex<TKey> : IDisposable
    where TKey : notnull
{
    // key -> the set of handles whose current payload yields that key. A HashSet keeps a handle from
    // being double-registered under one key and makes retirement on re-key an O(1) removal.
    private readonly Dictionary<TKey, HashSet<NodeHandle>> _byKey = new();

    // Detaches this index from the graph's change channel. Held as an Action so SecondaryIndex stays
    // free of the graph's TNode/TEdge parameters — the graph supplies the unsubscribe closure.
    private Action? _detach;

    // Constructed only by Graph.IndexNodesBy, which seeds and wires the change-channel subscription.
    internal SecondaryIndex()
    {
    }

    /// <summary>
    /// Every handle whose current payload yields <paramref name="key"/>, or an empty collection if
    /// none do. Returns a snapshot array, safe to hold and enumerate while the graph mutates.
    /// </summary>
    public IReadOnlyCollection<NodeHandle> Lookup(TKey key) =>
        _byKey.TryGetValue(key, out HashSet<NodeHandle>? handles)
            ? handles.ToArray()
            : Array.Empty<NodeHandle>();

    /// <summary>
    /// Detaches the index from the graph's change channel so later payload mutations are no longer
    /// tracked. Idempotent; after disposal the index is frozen at its last state.
    /// </summary>
    public void Dispose()
    {
        _detach?.Invoke();
        _detach = null;
    }

    // Registers a handle under a key (used to seed existing nodes and to record the new key on re-key).
    internal void Add(TKey key, NodeHandle handle)
    {
        if (!_byKey.TryGetValue(key, out HashSet<NodeHandle>? handles))
        {
            handles = new HashSet<NodeHandle>();
            _byKey[key] = handles;
        }
        handles.Add(handle);
    }

    // Retires a handle from a key, dropping the bucket entirely once empty so a stale key never
    // lingers as an empty entry (keeping Lookup's absent/empty answer honest).
    internal void Remove(TKey key, NodeHandle handle)
    {
        if (_byKey.TryGetValue(key, out HashSet<NodeHandle>? handles) && handles.Remove(handle)
            && handles.Count == 0)
        {
            _byKey.Remove(key);
        }
    }

    internal void SetDetach(Action detach) => _detach = detach;
}
