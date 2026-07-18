namespace GraphLibrary.Rules;

/// <summary>
/// The Region reach of a Rule (CONTEXT.md → Scope, ADR 0007): the answer to "which instances does
/// this rule count, and against how many bands?". All four vocabulary scopes — <c>named region</c>
/// (<see cref="Region"/>), <c>named set</c> (<see cref="RegionSet"/>), <c>each active region</c>
/// (<see cref="EachActiveRegion"/>) and <c>global</c> (<see cref="Global"/>) — compile to the one
/// engine primitive <em>filter instances by a region predicate, then count</em>
/// (<see cref="SelectionView{TNode,TEdge,TInstanceData}.Count"/>), invoked once per
/// <see cref="RegionBand"/> the scope resolves to.
/// </summary>
/// <remarks>
/// <para>
/// A scope is <em>not</em> a single predicate but a resolver from the selection's active regions to a
/// sequence of <see cref="RegionBand"/>s — one <c>filter-then-count</c> band each. Three of the four
/// scopes resolve to exactly one band (a whole-selection, single-region, or named-set filter);
/// <see cref="EachActiveRegion"/> is the sole fan-out — it resolves to one band <em>per active
/// region</em>, so a rule holds its bound separately in every region that exists (spec stories 68-69).
/// This uniform "resolve to bands, then count each" shape is what lets one
/// <see cref="Cardinality{TNode,TEdge,TInstanceData}.Check"/> serve all four scopes with no per-scope
/// branch (ADR 0007: four scopes, one primitive).
/// </para>
/// <para>
/// A named region or set that no instance carries simply resolves to a band that counts zero — the
/// engine owns no region registry, so an inactive region is not an error, only an empty count.
/// </para>
/// </remarks>
public sealed class Scope
{
    // Resolves this scope, against the view's active regions, to the bands to count. Internal: rules
    // in this assembly drive it; callers pick a scope by its named factory, never by predicate.
    private readonly Func<IReadOnlyCollection<Region>, IEnumerable<RegionBand>> _bands;

    private Scope(Func<IReadOnlyCollection<Region>, IEnumerable<RegionBand>> bands) => _bands = bands;

    /// <summary>
    /// Resolves this scope against <paramref name="activeRegions"/> into the bands to count — one
    /// <see cref="RegionBand"/> for every non-fan-out scope, one per active region for
    /// <see cref="EachActiveRegion"/>.
    /// </summary>
    internal IEnumerable<RegionBand> Bands(IReadOnlyCollection<Region> activeRegions) =>
        _bands(activeRegions);

    /// <summary>
    /// Every region — the rule counts across the whole selection as a single band, ignoring region
    /// boundaries. A single-instance limit and any other whole-selection count is a
    /// <see cref="Cardinality{TNode,TEdge,TInstanceData}"/> at this scope
    /// (see <see cref="Cardinality{TNode,TEdge,TInstanceData}.InstanceLimit"/>).
    /// </summary>
    public static Scope Global { get; } =
        new(static _ => [new RegionBand(static _ => true, "global")]);

    /// <summary>
    /// A single named region — the rule counts only instances carrying <paramref name="region"/>, as
    /// one band. The region need not be active; an inactive one counts zero.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="region"/> is the <see langword="default"/> (label-less) region — no instance
    /// can carry it, so scoping to it is a programmer bug caught here.
    /// </exception>
    public static Scope Region(Region region)
    {
        if (region == default)
        {
            throw new ArgumentException(
                "A region scope must name a region created from a label; the default Region is not usable.",
                nameof(region));
        }

        var band = new RegionBand(r => r == region, $"region {region}");
        return new Scope(_ => [band]);
    }

    /// <summary>
    /// A named set spanning several regions — the rule counts instances carrying any region in
    /// <paramref name="regions"/>, as one combined band (ADR 0007: naming the set gives the third
    /// level with zero tree machinery).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="regions"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="regions"/> is empty — an empty set counts nothing and is never a meaningful
    /// scope, so it is a programmer bug caught here.
    /// </exception>
    public static Scope RegionSet(IReadOnlyCollection<Region> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0)
        {
            throw new ArgumentException(
                "A region-set scope must name at least one region.", nameof(regions));
        }

        // Copy so a later mutation of the caller's collection cannot change what the scope counts.
        var set = new HashSet<Region>(regions);
        var band = new RegionBand(set.Contains, $"region set {{{string.Join(", ", set)}}}");
        return new Scope(_ => [band]);
    }

    /// <summary>
    /// Each active region, separately — the rule holds its bound in every region some instance carries,
    /// the sole fan-out scope. It resolves to one <see cref="RegionBand"/> per active region, so a
    /// per-region cardinality applies uniformly across regions the caller never had to enumerate
    /// (spec stories 68-69). An empty selection has no active regions and so imposes no band.
    /// </summary>
    public static Scope EachActiveRegion { get; } =
        new(static active => active.Select(r => new RegionBand(region => region == r, $"region {r}")));
}
