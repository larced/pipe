namespace GraphLibrary.Rules;

/// <summary>
/// The built-in <b>OR-group</b> rule "the selection must hold at least one occurrence of some
/// prototype in this group" (CONTEXT.md → Rule, ADR 0005: OR-groups stay declarative rule objects over
/// handles, not <c>TEdge</c>). A plain-constructor built-in over a set of prototype
/// <see cref="NodeHandle"/>s (spec story 55).
/// </summary>
/// <remarks>
/// The group is <em>hyper-edge / group-shaped</em> — it spans several prototypes at once — which is
/// exactly why it is a rule object and not an edge (ADR 0005). When none of the group's prototypes are
/// present the rule reports a single <see cref="ViolationKind.LowerBound"/> shortfall ("still needs one
/// of these"), resolvable by adding any group member; once one is present the rule is satisfied. It
/// says nothing about an upper bound — capping is a separate <see cref="ConflictGroup{TNode,TEdge,TInstanceData}"/>
/// or <see cref="Cardinality{TNode,TEdge,TInstanceData}"/>, kept orthogonal so rules stay flat and
/// conjunctive. Membership is counted across the whole selection, ignoring regions; a region-scoped
/// OR-group is a custom rule over the <see cref="SelectionView{TNode,TEdge,TInstanceData}"/>.
/// </remarks>
public sealed class OneOf<TNode, TEdge, TInstanceData> : IRule<TNode, TEdge, TInstanceData>,
    IHandleReferencing
{
    private readonly NodeHandle[] _members;

    /// <summary>
    /// Requires at least one occurrence of some prototype in <paramref name="prototypes"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="prototypes"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prototypes"/> is empty — an OR over nothing is never satisfiable and is a
    /// programmer bug, caught at construction.
    /// </exception>
    public OneOf(params NodeHandle[] prototypes)
    {
        ArgumentNullException.ThrowIfNull(prototypes);
        if (prototypes.Length == 0)
        {
            throw new ArgumentException("A OneOf group must name at least one prototype.", nameof(prototypes));
        }

        _members = [.. prototypes];
    }

    /// <summary>The prototypes in this OR-group, in construction order.</summary>
    public IReadOnlyList<NodeHandle> Prototypes => _members;

    /// <summary>The group members this rule references, for handle-indexed evaluation (ADR 0005).</summary>
    IEnumerable<NodeHandle> IHandleReferencing.ReferencedHandles => _members;

    /// <inheritdoc/>
    public IEnumerable<Violation> Check(SelectionView<TNode, TEdge, TInstanceData> view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var present = view.Count(static _ => true, i => _members.Contains(i.Prototype));
        if (present == 0)
        {
            yield return new Violation(
                ViolationKind.LowerBound,
                $"OneOf: the selection must hold at least one of the {_members.Length} group prototype(s), found none.");
        }
    }
}
