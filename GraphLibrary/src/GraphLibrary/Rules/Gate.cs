namespace GraphLibrary.Rules;

/// <summary>
/// One opt-in precondition that a <see cref="IGatingRule{TNode,TEdge,TInstanceData}"/> declares: the
/// <see cref="Gated"/> prototype is <b>unavailable until</b> <see cref="IsSatisfied"/> holds over the
/// <em>current</em> selection (CONTEXT.md → Gating, ADR 0005). This is the strict posture — a candidate
/// is blocked outright — as opposed to the default gentle/eventual posture, where an unmet constraint
/// merely leaves the selection invalid-until-satisfied (a lower-bound <see cref="Violation"/> from a
/// normal rule).
/// </summary>
/// <remarks>
/// A gate is evaluated by <see cref="Evaluator.Availability{TNode,TEdge,TInstanceData}"/> against the
/// selection as it stands, <em>before</em> the candidate is simulated — it answers "is the ground ready
/// for this node yet", which is why the precondition reads only the current
/// <see cref="SelectionView{TNode,TEdge,TInstanceData}"/>. Gates never participate in
/// <see cref="Evaluator.Check{TNode,TEdge,TInstanceData}"/>: gating governs the order of adding, not the
/// validity of what has been added.
/// </remarks>
public sealed class Gate<TNode, TEdge, TInstanceData>
{
    private readonly Func<SelectionView<TNode, TEdge, TInstanceData>, bool> _precondition;

    /// <summary>
    /// Declares that <paramref name="gated"/> is unavailable until <paramref name="precondition"/> holds
    /// over the current selection, describing the requirement with <paramref name="requirement"/> for a
    /// "why-not" preview.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="precondition"/> or <paramref name="requirement"/> is null.
    /// </exception>
    public Gate(
        NodeHandle gated,
        Func<SelectionView<TNode, TEdge, TInstanceData>, bool> precondition,
        string requirement)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(requirement);

        Gated = gated;
        _precondition = precondition;
        Requirement = requirement;
    }

    /// <summary>The prototype this gate guards — the node that stays unavailable until the precondition holds.</summary>
    public NodeHandle Gated { get; }

    /// <summary>A human-readable description of the precondition, surfaced on a <see cref="GateFailure"/>.</summary>
    public string Requirement { get; }

    /// <summary>
    /// Whether the precondition holds over <paramref name="view"/> — the current selection. When it does
    /// not, the gate contributes a <see cref="GateFailure"/> and the gated candidate is blocked.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public bool IsSatisfied(SelectionView<TNode, TEdge, TInstanceData> view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _precondition(view);
    }
}
