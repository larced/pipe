namespace GraphLibrary.Rules;

/// <summary>
/// An optional capability an <see cref="IRule{TNode,TEdge,TInstanceData}"/> implements to opt into
/// <b>Gating</b> — declaring one or more <see cref="Gate{TNode,TEdge,TInstanceData}"/>s that make a node
/// unavailable until a precondition over the current selection holds (CONTEXT.md → Gating, spec story
/// 62, ADR 0005). A rule that does not implement it is purely gentle/eventual: its findings only make
/// the selection invalid-until-satisfied, never blocking an add.
/// </summary>
/// <remarks>
/// <para>
/// Gating is <b>opt-in and orthogonal to <see cref="IRule{TNode,TEdge,TInstanceData}.Check"/></b>. A
/// gating rule still implements <see cref="IRule{TNode,TEdge,TInstanceData}"/> so it travels in the one
/// flat rule set, but its gates surface only through
/// <see cref="Evaluator.Availability{TNode,TEdge,TInstanceData}"/> (as
/// <see cref="GateFailure"/>s), never through <see cref="Evaluator.Check{TNode,TEdge,TInstanceData}"/> —
/// a gate governs <em>when</em> a node may be added, not whether the current selection is valid. A rule
/// whose <see cref="IRule{TNode,TEdge,TInstanceData}.Check"/> reports nothing and that only gates (like
/// the built-in <see cref="GatedBy{TNode,TEdge,TInstanceData}"/>) is a perfectly ordinary member of the
/// set.
/// </para>
/// <para>
/// The Evaluator groups every declared gate by its <see cref="Gate{TNode,TEdge,TInstanceData}.Gated"/>
/// handle and, for a candidate of that prototype, blocks it on any gate whose precondition the current
/// selection does not satisfy — the gate half of <c>Availability = derived-blocks ∪ gate-failures</c>.
/// </para>
/// </remarks>
public interface IGatingRule<TNode, TEdge, TInstanceData>
{
    /// <summary>
    /// The gates this rule declares. Each names the prototype it guards and a precondition over the
    /// current selection; an empty sequence means the rule opts into the capability but gates nothing.
    /// </summary>
    IEnumerable<Gate<TNode, TEdge, TInstanceData>> Gates { get; }
}
