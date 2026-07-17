namespace GraphLibrary.Rules;

/// <summary>
/// The Region reach of a Rule (CONTEXT.md → Scope, ADR 0007): the answer to "which instances does
/// this rule count?". A scope is nothing but a <see cref="Region"/> predicate — the four vocabulary
/// scopes (<c>named region</c>, <c>named set</c>, <c>each active region</c>, <c>global</c>) all
/// compile to the one engine primitive <em>filter instances by a region predicate, then count</em>
/// (<see cref="SelectionView{TNode,TEdge,TInstanceData}.Count"/>).
/// </summary>
/// <remarks>
/// This tracer slice ships only <see cref="Global"/>; the region-targeting scopes (a single region, a
/// named set, and the per-region fan-out of <c>each active region</c>) are additive later and reuse
/// the same predicate-then-count primitive with no engine change.
/// </remarks>
public sealed class Scope
{
    // The region filter this scope compiles to. Internal: rules in this assembly hand it to the
    // SelectionView's count primitive; callers pick a scope by its named factory, never by predicate.
    internal Func<Region, bool> Matches { get; }

    private Scope(Func<Region, bool> matches) => Matches = matches;

    /// <summary>
    /// Every region — the rule counts across the whole selection, ignoring region boundaries. A
    /// single-instance limit and any other whole-selection count is a <see cref="Cardinality{TNode,TEdge,TInstanceData}"/>
    /// at this scope.
    /// </summary>
    public static Scope Global { get; } = new(static _ => true);
}
