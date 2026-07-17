namespace GraphLibrary.Rules;

/// <summary>
/// The rule-evaluation engine over <c>(graph, selection, rules)</c> (CONTEXT.md → Evaluator, ADR
/// 0005). This tracer slice ships <see cref="Check{TNode,TEdge,TInstanceData}"/> — "is this selection
/// valid, and if not, why not"; the Availability oracle ("what may I add next") arrives in a later
/// ticket over the same classified violations. It only ever <b>Checks</b>; it is <b>not a solver</b> —
/// it never completes, enumerates, or satisfies a selection.
/// </summary>
public static class Evaluator
{
    /// <summary>
    /// Evaluates every rule in <paramref name="rules"/> against <paramref name="selection"/> (over
    /// <paramref name="graph"/>) and returns the flat <b>union</b> of their violations. The selection
    /// is valid exactly when the result is empty.
    /// </summary>
    /// <remarks>
    /// Rules compose flat, conjunctive, and order-free: every rule is evaluated (no short-circuit) and
    /// its findings are concatenated with no precedence — the result is a set-union in spirit, so the
    /// order the rules are supplied in never changes <em>which</em> violations appear. The base graph
    /// is only ever read (through the view's read-only surface), so Check cannot mutate it.
    /// </remarks>
    public static IReadOnlyList<Violation> Check<TNode, TEdge, TInstanceData>(
        IReadableGraph<TNode, TEdge> graph,
        Selection<TInstanceData> selection,
        IEnumerable<IRule<TNode, TEdge, TInstanceData>> rules)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rules);

        var view = new SelectionView<TNode, TEdge, TInstanceData>(graph, selection);
        var violations = new List<Violation>();

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));
            violations.AddRange(rule.Check(view));
        }

        return violations;
    }
}
