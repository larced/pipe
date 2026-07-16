namespace GraphLibrary;

/// <summary>
/// The internal seam that lets the layered <c>GraphLibrary.Traversal</c> API observe ticket 06's
/// monotonic structural-version counter (spec stories 78–84) so a lazy walk can fail fast when the
/// graph is structurally mutated mid-iteration (spec story 40) — <em>without</em> widening the
/// public <see cref="IReadableGraph{TNode,TEdge}"/> read surface with an implementation detail
/// (ADR 0004). Internal on purpose: only same-assembly traversal code casts to it, and a caller who
/// never imports <c>Traversal</c> never encounters it (spec story 41).
/// </summary>
/// <remarks>
/// A read surface that carries no structural version (a future immutable snapshot, say) simply does
/// not implement this, and traversal over it skips the fail-fast check — there is nothing to race.
/// Reading the counter is a pure read, so it does not disturb the concurrent-read guarantee
/// <see cref="Graph{TNode,TEdge}"/> relies on.
/// </remarks>
internal interface IStructurallyVersioned
{
    /// <summary>The current value of the structural-version counter — see ticket 06.</summary>
    int StructuralVersion { get; }
}
