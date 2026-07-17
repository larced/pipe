namespace GraphLibrary.Rules;

/// <summary>
/// The rule-evaluation extension point (CONTEXT.md → Rule, ADR 0005): a validity constraint over a
/// Selection, expressed as a classified <see cref="Check"/> predicate. Built-in rules (e.g.
/// <see cref="Cardinality{TNode,TEdge,TInstanceData}"/>) and caller-written custom rules implement the
/// <em>same</em> interface and are evaluated identically by <see cref="Evaluator"/> — there is no
/// privileged built-in path.
/// </summary>
/// <remarks>
/// Rules compose <b>flat, conjunctive, and order-free</b>: a rule reads only the
/// <see cref="SelectionView{TNode,TEdge,TInstanceData}"/> and returns its own findings, never
/// consulting, ordering, or suppressing another rule. <see cref="Evaluator"/> takes the union — so a
/// rule must report <em>all</em> the violations it finds (not stop at the first), and returns an empty
/// sequence when satisfied. A rule governs a Selection, distinct from a graph <c>Validator</c>, which
/// governs topology.
/// </remarks>
public interface IRule<TNode, TEdge, TInstanceData>
{
    /// <summary>
    /// Inspects <paramref name="view"/> and returns every <see cref="Violation"/> this rule finds,
    /// each classified <see cref="ViolationKind.LowerBound"/> or <see cref="ViolationKind.UpperBound"/>;
    /// an empty sequence means the rule is satisfied. Must not mutate anything — the view's graph is a
    /// read-only surface, and Check is a pure query.
    /// </summary>
    IEnumerable<Violation> Check(SelectionView<TNode, TEdge, TInstanceData> view);
}
