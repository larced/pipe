namespace GraphLibrary.Rules;

/// <summary>
/// A mutable overlay of <see cref="Instance{TInstanceData}"/> records layered over a base
/// <see cref="Graph{TNode,TEdge}"/>, representing a candidate configuration (CONTEXT.md → Selection,
/// ADR 0005). <see cref="Add"/> records an occurrence of a prototype node and returns its
/// <see cref="InstanceId"/>; <see cref="Remove"/> retracts one. This ticket (#24) is a tracer-bullet
/// vertical slice — the overlay itself; the rule-evaluation engine (Check / Availability) and the
/// region-aware selection view arrive in later tickets.
/// </summary>
/// <remarks>
/// <para>
/// <b>The overlay never mutates the base graph.</b> This is the load-bearing design win of ADR 0005,
/// and it holds here for a structural reason: a selection is a <em>separate</em> structure that only
/// stores prototype <see cref="NodeHandle"/> references plus overlay data — it has no reference to a
/// graph to mutate. The base graph is supplied separately to the Evaluator (over
/// <c>(graph, selection, rules)</c>) in a later ticket; validating that a prototype belongs to a given
/// graph is that engine's job at Check/Availability time. Consequently instance data
/// (<typeparamref name="TInstanceData"/>) carries no <c>TNode</c>, so the base graph never churns
/// through selection activity (spec story 49).
/// </para>
/// <para>
/// <b>Regions have no registry</b> (ADR 0007): a <see cref="Region"/> exists iff some live instance
/// carries it, exposed by <see cref="ActiveRegions"/>. Any region is legal and springs into being on
/// first <see cref="Add"/>; exactly one region per instance keeps per-region counting unambiguous.
/// </para>
/// <para>
/// Following the library's convention (Graph is caller-synchronized, not internally thread-safe), a
/// selection is caller-synchronized too. Materialized reads (<see cref="Instances"/>,
/// <see cref="ActiveRegions"/>) are eager snapshots, safe to hold across later mutation.
/// </para>
/// </remarks>
public sealed class Selection<TInstanceData>
{
    // Distinct id per selection, stamped into every InstanceId this selection mints so an id from
    // another selection is rejected rather than aliasing an unrelated instance (mirrors Graph's
    // per-instance graph id / ADR 0003). Starts at 1 (Interlocked.Increment from 0), so a default
    // InstanceId's stamp of 0 never matches a real selection. Interlocked keeps ids unique even if
    // selections are constructed concurrently (construction itself needs no other sync).
    private static int _nextSelectionId;

    private readonly int _selectionId = System.Threading.Interlocked.Increment(ref _nextSelectionId);

    // Monotonic instance number, never reused — a removed id stays stale forever, so an id can never
    // be resurrected onto a different occurrence. Insertion order is preserved by the dictionary
    // until a removal, but no ordering is promised on Instances.
    private int _nextInstanceValue;

    private readonly Dictionary<InstanceId, Instance<TInstanceData>> _instances = new();

    /// <summary>The number of live instances in this selection.</summary>
    public int Count => _instances.Count;

    /// <summary>
    /// Records an occurrence of the prototype <paramref name="prototype"/> carrying
    /// <paramref name="data"/> and tagged with exactly one <paramref name="region"/>, and returns its
    /// stable <see cref="InstanceId"/> (spec story 46). Calling it again with the same prototype
    /// produces a distinct occurrence with a new id (story 47) — "two of this product". The prototype
    /// is stored as an opaque handle; it is validated against a specific graph by the Evaluator later,
    /// not here.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="region"/> is a <see langword="default"/> (label-less) region — every instance
    /// must carry a real region so per-region counting stays unambiguous (spec story 66).
    /// </exception>
    public InstanceId Add(NodeHandle prototype, Region region, TInstanceData data)
    {
        if (region == default)
        {
            throw new ArgumentException(
                "An instance must carry a region created from a label; the default Region is not usable.",
                nameof(region));
        }

        var id = new InstanceId(++_nextInstanceValue, _selectionId);
        _instances.Add(id, new Instance<TInstanceData>(id, prototype, region, data));
        return id;
    }

    /// <summary>
    /// Retracts the instance identified by <paramref name="id"/> (spec story 48). Returns
    /// <see langword="true"/> if it existed, <see langword="false"/> if it was already gone
    /// (idempotent).
    /// </summary>
    /// <exception cref="InvalidInstanceIdException">
    /// <paramref name="id"/> was minted by a different selection — cross-selection misuse, a
    /// programmer bug that fails fast rather than silently aliasing an unrelated instance (ADR 0003
    /// ethos). An id from <em>this</em> selection whose instance was already removed is not misuse; it
    /// returns <see langword="false"/>.
    /// </exception>
    public bool Remove(InstanceId id)
    {
        RequireOwnSelection(id);
        return _instances.Remove(id);
    }

    /// <summary>
    /// Looks up the instance identified by <paramref name="id"/>. Returns <see langword="false"/> if
    /// it is not (or no longer) present.
    /// </summary>
    /// <exception cref="InvalidInstanceIdException">
    /// <paramref name="id"/> was minted by a different selection (see <see cref="Remove"/>).
    /// </exception>
    public bool TryGet(InstanceId id, out Instance<TInstanceData> instance)
    {
        RequireOwnSelection(id);
        return _instances.TryGetValue(id, out instance!);
    }

    /// <summary>
    /// An eager snapshot of the live instances, in no promised order. Safe to hold and enumerate
    /// across later selection mutation (spec story 39 ethos).
    /// </summary>
    public IReadOnlyCollection<Instance<TInstanceData>> Instances => _instances.Values.ToArray();

    /// <summary>
    /// An eager snapshot of the regions currently carried by at least one live instance — the overlay
    /// has no region registry, so a region exists iff it appears here (spec stories 64-65). Distinct;
    /// in no promised order.
    /// </summary>
    public IReadOnlyCollection<Region> ActiveRegions =>
        _instances.Values.Select(i => i.Region).Distinct().ToArray();

    // Cross-selection guard, mirroring Graph.RequireOwnGraph (which throws InvalidHandleException.
    // CrossGraph): an id whose stamp is not ours was minted by another selection (or is a default id,
    // stamp 0) and is unambiguous misuse — throw the dedicated cross-selection exception. An id with
    // our stamp but no live instance is a legitimately stale id and left to the caller (idempotent).
    private void RequireOwnSelection(InstanceId id)
    {
        if (id.SelectionId != _selectionId)
        {
            throw InvalidInstanceIdException.CrossSelection();
        }
    }
}
