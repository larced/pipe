namespace GraphLibrary.Rules;

/// <summary>
/// The built-in gating rule "the <see cref="Gated"/> prototype is unavailable until at least one
/// occurrence of every <see cref="Prerequisite"/> is already present in the selection" (CONTEXT.md →
/// Gating, spec story 62, ADR 0005). It is the strict, order-imposing counterpart of the gentle
/// <see cref="RequiresEdge{TNode,TEdge,TInstanceData}"/>: where a requires-dependency lets you add the
/// dependant freely and merely reports the selection invalid until the target is added, a
/// <see cref="GatedBy{TNode,TEdge,TInstanceData}"/> forbids adding the dependant at all until the
/// prerequisite is in place.
/// </summary>
/// <remarks>
/// It reports <b>no</b> <see cref="Violation"/> from <see cref="Check"/> — gating is an availability
/// posture, not a validity one — so it exists purely to contribute
/// <see cref="Gate{TNode,TEdge,TInstanceData}"/>s (spec story 63:
/// <c>Availability = derived-blocks ∪ gate-failures</c>). Each prerequisite becomes its own gate over
/// the same gated node, so a node waiting on several prerequisites reports one
/// <see cref="GateFailure"/> per missing one, mirroring the AND-shape of
/// <see cref="RequiresEdge{TNode,TEdge,TInstanceData}"/>. Presence is measured across the whole
/// selection, ignoring regions.
/// </remarks>
public sealed class GatedBy<TNode, TEdge, TInstanceData>
    : IRule<TNode, TEdge, TInstanceData>, IGatingRule<TNode, TEdge, TInstanceData>
{
    private readonly NodeHandle[] _prerequisites;

    /// <summary>
    /// Gates <paramref name="gated"/> behind the presence of every prototype in
    /// <paramref name="prerequisites"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="prerequisites"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prerequisites"/> is empty — a gate behind nothing gates nothing and is a
    /// programmer bug, caught at construction.
    /// </exception>
    public GatedBy(NodeHandle gated, params NodeHandle[] prerequisites)
    {
        ArgumentNullException.ThrowIfNull(prerequisites);
        if (prerequisites.Length == 0)
        {
            throw new ArgumentException(
                "A GatedBy rule must name at least one prerequisite prototype.", nameof(prerequisites));
        }

        Gated = gated;
        _prerequisites = [.. prerequisites];
    }

    /// <summary>The prototype this rule gates — unavailable until every prerequisite is present.</summary>
    public NodeHandle Gated { get; }

    /// <summary>The prototypes that must all be present before <see cref="Gated"/> becomes available.</summary>
    public IReadOnlyList<NodeHandle> Prerequisites => _prerequisites;

    /// <summary>
    /// Gating never touches validity, so this always reports no violations (spec story 63). The rule's
    /// effect is delivered entirely through <see cref="Gates"/>.
    /// </summary>
    public IEnumerable<Violation> Check(SelectionView<TNode, TEdge, TInstanceData> view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return [];
    }

    /// <inheritdoc/>
    public IEnumerable<Gate<TNode, TEdge, TInstanceData>> Gates =>
        _prerequisites.Select(prerequisite => new Gate<TNode, TEdge, TInstanceData>(
            Gated,
            view => view.Count(static _ => true, i => i.Prototype == prerequisite) > 0,
            "requires an occurrence of a prerequisite prototype to be selected first"));
}
