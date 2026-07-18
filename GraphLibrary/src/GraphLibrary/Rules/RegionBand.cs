namespace GraphLibrary.Rules;

/// <summary>
/// One <c>filter-then-count</c> band a <see cref="Scope"/> resolves to (ADR 0007): a region
/// <see cref="Matches"/> predicate handed to <see cref="SelectionView{TNode,TEdge,TInstanceData}.Count"/>,
/// plus a human-readable <see cref="Description"/> naming the band for a rule's violation message.
/// </summary>
/// <remarks>
/// Internal because it is the wiring between a scope and the count primitive, not part of the caller's
/// vocabulary — callers pick a scope by its named factory (<see cref="Scope.Global"/> etc.). A
/// non-fan-out scope resolves to a single band; <see cref="Scope.EachActiveRegion"/> resolves to one
/// per active region, which is why the description is per-band rather than per-scope.
/// </remarks>
/// <param name="Matches">The region predicate this band counts over.</param>
/// <param name="Description">A short label naming the band, e.g. <c>"global"</c> or <c>"region FrontAxle"</c>.</param>
internal sealed record RegionBand(Func<Region, bool> Matches, string Description);
