namespace GraphLibrary.Rules;

/// <summary>
/// The region-aware read surface every <see cref="IRule{TNode,TEdge,TInstanceData}"/> reads
/// (CONTEXT.md → Selection view, ADR 0007): the base graph, the active <see cref="Region"/>s, an
/// instance's region, and the one counting primitive that all four rule scopes compile to. It is the
/// <em>sufficient substrate</em> a rule needs to decide validity without any library change — the
/// firm addition ADR 0007 makes to ADR 0005.
/// </summary>
/// <remarks>
/// <para>
/// A view is an eager <b>snapshot</b> of a <see cref="Selection{TInstanceData}"/> taken at
/// construction (matching the selection's own snapshot ethos), so a rule reads a stable picture even
/// if the selection mutates afterward. It also fronts the base graph as the lean
/// <see cref="IReadableGraph{TNode,TEdge}"/> read surface — a <b>read-only</b> reference, which is why
/// <see cref="Evaluator.Check{TNode,TEdge,TInstanceData}"/> cannot mutate the base graph — so an
/// edge-reading rule participates through the very same view as a count-only rule, with no separate
/// mechanism (ADR 0005: built-in and custom rules participate identically).
/// </para>
/// <para>
/// <b>The one primitive</b> is <see cref="Count"/> — <em>filter instances by a region predicate, then
/// count</em> (ADR 0007). <see cref="GlobalCount"/>, <see cref="RegionCount"/>, and
/// <see cref="RegionSetCount"/> are the per-region / cross-region / global counts of spec story 70,
/// each a thin call into that primitive; the primitive's optional <c>among</c> argument narrows the
/// count to a subset (e.g. occurrences of one prototype) within the region scope.
/// </para>
/// </remarks>
public sealed class SelectionView<TNode, TEdge, TInstanceData>
{
    private readonly Instance<TInstanceData>[] _instances;
    private readonly Dictionary<InstanceId, Region> _regionOf;

    /// <summary>
    /// Snapshots <paramref name="selection"/> and fronts <paramref name="graph"/> for rules to read.
    /// Callers rarely construct a view directly — <see cref="Evaluator"/> builds one per Check — but
    /// the constructor is public so a rule author can unit-test a custom rule against a hand-built view.
    /// </summary>
    public SelectionView(IReadableGraph<TNode, TEdge> graph, Selection<TInstanceData> selection)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selection);

        Graph = graph;
        _instances = [.. selection.Instances];
        _regionOf = _instances.ToDictionary(i => i.Id, i => i.Region);
    }

    /// <summary>
    /// The base graph as its lean read surface, for a rule that reads topology (e.g. the
    /// <c>requires</c>-edge dependency rule of a later ticket). Read-only: a rule — and therefore
    /// Check — cannot mutate the base graph through it.
    /// </summary>
    public IReadableGraph<TNode, TEdge> Graph { get; }

    /// <summary>The live instances of the snapshotted selection, in no promised order.</summary>
    public IReadOnlyCollection<Instance<TInstanceData>> Instances => _instances;

    /// <summary>
    /// The regions carried by at least one snapshotted instance — the selection has no region
    /// registry, so a region is active iff it appears here (ADR 0007). Distinct; in no promised order.
    /// </summary>
    public IReadOnlyCollection<Region> ActiveRegions =>
        _instances.Select(i => i.Region).Distinct().ToArray();

    /// <summary>
    /// The <see cref="Region"/> the instance identified by <paramref name="id"/> carries.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// No live instance in this view has that id (it was never added, has been removed, or belongs to
    /// another selection) — a programmer error, so it fails fast rather than inventing a region.
    /// </exception>
    public Region RegionOf(InstanceId id)
    {
        if (!_regionOf.TryGetValue(id, out var region))
        {
            throw new ArgumentException(
                "No instance with that id is present in this view.", nameof(id));
        }

        return region;
    }

    /// <summary>
    /// The one engine primitive (ADR 0007): the number of instances whose region satisfies
    /// <paramref name="scope"/> and, if given, that also satisfy <paramref name="among"/>. Every rule
    /// scope is a choice of <paramref name="scope"/>; <paramref name="among"/> narrows <em>what</em> is
    /// counted within that scope (e.g. occurrences of one prototype).
    /// </summary>
    public int Count(Func<Region, bool> scope, Func<Instance<TInstanceData>, bool>? among = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var count = 0;
        foreach (var instance in _instances)
        {
            if (scope(instance.Region) && (among is null || among(instance)))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The global count: every instance in the selection (spec story 70).</summary>
    public int GlobalCount => Count(static _ => true);

    /// <summary>The per-region count: instances carrying exactly <paramref name="region"/>.</summary>
    public int RegionCount(Region region) => Count(r => r == region);

    /// <summary>
    /// The cross-region count: instances carrying any region in <paramref name="regions"/> (a named
    /// set spanning several regions).
    /// </summary>
    public int RegionSetCount(IReadOnlyCollection<Region> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return Count(regions.Contains);
    }
}
