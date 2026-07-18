namespace GraphLibrary.Rules;

/// <summary>
/// The built-in dependency rule "for every <em>requires</em>-typed edge in the base graph, if the
/// source prototype is selected then the target prototype must be selected too" (CONTEXT.md → Rule,
/// ADR 0005: <b>edges feed rules but are not rules</b> — option C). It is the one built-in that
/// <em>reads the base graph's topology</em>: a domain models "A requires B" as a <c>TEdge</c> from A to
/// B, and this single rule turns every such edge into an AND-dependency (spec story 75).
/// </summary>
/// <remarks>
/// <para>
/// The library is domain-agnostic and cannot know which <c>TEdge</c> payloads mean "requires", so the
/// caller supplies an <c>isRequiresEdge</c> predicate over the edge payload; the rule reads only edges
/// it accepts. Dependency is an <b>AND</b> across edges: a prototype with several outgoing requires-edges
/// must have <em>all</em> its targets present, and each unmet edge is reported as its own
/// <see cref="ViolationKind.LowerBound"/> shortfall ("still needs the target"), resolvable by adding the
/// target — never suppressing another edge's finding (ADR 0005: flat, conjunctive, order-free).
/// </para>
/// <para>
/// Direction: an edge <c>A → B</c> means "A requires B", so the dependency fires when an occurrence of
/// the <em>source</em> exists and no occurrence of the <em>target</em> does. Presence is measured
/// across the whole selection, ignoring regions — a region-scoped dependency is a custom rule over the
/// <see cref="SelectionView{TNode,TEdge,TInstanceData}"/>. The rule only reads
/// <see cref="SelectionView{TNode,TEdge,TInstanceData}.Graph"/> (a read-only surface), so Check never
/// mutates the base graph.
/// </para>
/// <para>
/// It deliberately does <b>not</b> implement <see cref="IHandleReferencing"/>: the handles it depends
/// on are discovered from the graph at Check time, not fixed at construction, so it is always
/// evaluated rather than handle-indexed (see <see cref="RuleIndex{TNode,TEdge,TInstanceData}"/>).
/// </para>
/// </remarks>
public sealed class RequiresEdge<TNode, TEdge, TInstanceData> : IRule<TNode, TEdge, TInstanceData>
{
    private readonly Func<TEdge, bool> _isRequiresEdge;

    /// <summary>
    /// Reads every base-graph edge whose payload satisfies <paramref name="isRequiresEdge"/> as an
    /// AND-dependency from its source prototype to its target.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="isRequiresEdge"/> is null.</exception>
    public RequiresEdge(Func<TEdge, bool> isRequiresEdge)
    {
        ArgumentNullException.ThrowIfNull(isRequiresEdge);
        _isRequiresEdge = isRequiresEdge;
    }

    /// <inheritdoc/>
    public IEnumerable<Violation> Check(SelectionView<TNode, TEdge, TInstanceData> view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var graph = view.Graph;

        // Which prototypes the selection holds at least one occurrence of — the presence set every
        // dependency edge tests against. Built once so the rule stays off O(edges × instances).
        var present = new HashSet<NodeHandle>();
        foreach (var instance in view.Instances)
        {
            present.Add(instance.Prototype);
        }

        foreach (var edge in graph.Edges)
        {
            if (!_isRequiresEdge(graph.GetEdgePayload(edge)))
            {
                continue;
            }

            var source = graph.GetSource(edge);
            var target = graph.GetTarget(edge);

            // "source requires target": the dependency only bites once the source is actually selected.
            if (present.Contains(source) && !present.Contains(target))
            {
                yield return new Violation(
                    ViolationKind.LowerBound,
                    "RequiresEdge: a selected prototype requires another that is absent; add the required prototype.");
            }
        }
    }
}
