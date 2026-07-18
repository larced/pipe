namespace GraphLibrary.Rules;

/// <summary>
/// The built-in <b>conflict-group</b> rule "the selection must hold at most one occurrence drawn from
/// this group of mutually-exclusive prototypes" (CONTEXT.md → Rule, ADR 0005: conflict-groups stay
/// declarative rule objects over handles, not <c>TEdge</c>). A plain-constructor built-in over a set
/// of prototype <see cref="NodeHandle"/>s (spec story 56).
/// </summary>
/// <remarks>
/// Like <see cref="OneOf{TNode,TEdge,TInstanceData}"/> it is <em>group-shaped</em> — an exclusion over
/// several prototypes at once — hence a rule object rather than an edge (ADR 0005). It is an exclusion
/// between <em>alternatives</em>: it counts how many <em>distinct</em> group members are present across
/// the whole selection, and the moment two or more are, reports a single
/// <see cref="ViolationKind.UpperBound"/> breach ("too many / conflicts"), which adding more can only
/// worsen. Two occurrences of the <em>same</em> member are <em>not</em> a conflict — that is a quantity
/// concern for <see cref="Cardinality{TNode,TEdge,TInstanceData}.InstanceLimit"/>, kept orthogonal here
/// — the group forbids mixing distinct alternatives, not repeating one. It imposes no lower bound
/// (requiring one is a separate <see cref="OneOf{TNode,TEdge,TInstanceData}"/>), keeping rules flat and
/// orthogonal. Exclusion is counted globally; a region-scoped conflict is a custom rule over the
/// <see cref="SelectionView{TNode,TEdge,TInstanceData}"/>.
/// </remarks>
public sealed class ConflictGroup<TNode, TEdge, TInstanceData> : IRule<TNode, TEdge, TInstanceData>,
    IHandleReferencing
{
    private readonly NodeHandle[] _members;

    /// <summary>
    /// Requires at most one occurrence drawn from <paramref name="prototypes"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="prototypes"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prototypes"/> is empty — an exclusion over nothing constrains nothing and is a
    /// programmer bug, caught at construction.
    /// </exception>
    public ConflictGroup(params NodeHandle[] prototypes)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        if (prototypes.Length == 0)
        {
            throw new ArgumentException(
                "A ConflictGroup must name at least one prototype.", nameof(prototypes));
        }

        _members = [.. prototypes];
    }

    /// <summary>The mutually-exclusive prototypes in this group, in construction order.</summary>
    public IReadOnlyList<NodeHandle> Prototypes => _members;

    /// <summary>The group members this rule references, for handle-indexed evaluation (ADR 0005).</summary>
    IEnumerable<NodeHandle> IHandleReferencing.ReferencedHandles => _members;

    /// <inheritdoc/>
    public IEnumerable<Violation> Check(SelectionView<TNode, TEdge, TInstanceData> view)
    {
        ArgumentNullException.ThrowIfNull(view);

        // Count distinct members present, not total occurrences: the group excludes mixing distinct
        // alternatives, while repeating one alternative is a quantity concern for InstanceLimit.
        var distinctPresent = _members.Count(
            member => view.Count(static _ => true, i => i.Prototype == member) > 0);
        if (distinctPresent > 1)
        {
            yield return new Violation(
                ViolationKind.UpperBound,
                $"ConflictGroup: the selection may hold at most one of the {_members.Length} mutually-exclusive prototype(s), found {distinctPresent} present.");
        }
    }
}
