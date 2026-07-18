namespace GraphLibrary.Rules;

/// <summary>
/// The built-in rule "the selection must hold between <see cref="Min"/> and <see cref="Max"/>
/// occurrences of a given <see cref="Prototype"/>, within a region <see cref="Scope"/>" (CONTEXT.md →
/// Rule / Scope, ADR 0007). One built-in absorbs the prototype's separate per-region cardinality and
/// instance-limit rules: it supports all four scopes, and a single-instance limit is nothing but this
/// rule at <see cref="Scope.Global"/> with band <c>[0,1]</c> (spec story 54, exposed as
/// <see cref="InstanceLimit"/>).
/// </summary>
/// <remarks>
/// Its <see cref="Check"/> is the canonical use of the one primitive — the <see cref="Scope"/> resolves
/// to one or more <see cref="RegionBand"/>s, the prototype supplies the <c>among</c> narrowing,
/// and the view counts each band. A count below <see cref="Min"/> is a
/// <see cref="ViolationKind.LowerBound"/> shortfall ("still needs more"); a count above
/// <see cref="Max"/> is a <see cref="ViolationKind.UpperBound"/> breach ("too many"). Under
/// <see cref="Scope.EachActiveRegion"/> the fan-out means a region short of <see cref="Min"/> and a
/// different region over <see cref="Max"/> both report — Check emits <em>every</em> band's finding, so
/// the rule stays flat and conjunctive across regions (ADR 0005).
/// </remarks>
public sealed class Cardinality<TNode, TEdge, TInstanceData> : IRule<TNode, TEdge, TInstanceData>,
    IHandleReferencing
{
    /// <summary>The region reach counted — any of the four <see cref="Rules.Scope"/> scopes.</summary>
    public Scope Scope { get; }

    /// <summary>The prototype node whose occurrences are counted.</summary>
    public NodeHandle Prototype { get; }

    /// <summary>The inclusive lower bound on the count (a shortfall below it is lower-bound).</summary>
    public int Min { get; }

    /// <summary>The inclusive upper bound on the count (an excess above it is upper-bound).</summary>
    public int Max { get; }

    /// <summary>
    /// Requires between <paramref name="min"/> and <paramref name="max"/> (inclusive) occurrences of
    /// <paramref name="prototype"/> within <paramref name="scope"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="min"/> is negative, or <paramref name="max"/> is less than <paramref name="min"/>
    /// — an unsatisfiable band is a programmer bug, caught at construction.
    /// </exception>
    public Cardinality(Scope scope, NodeHandle prototype, int min, int max)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);

        Scope = scope;
        Prototype = prototype;
        Min = min;
        Max = max;
    }

    /// <summary>
    /// A global instance limit: at most <paramref name="max"/> occurrences of <paramref name="prototype"/>
    /// across the whole selection — a thin convenience over a <see cref="Scope.Global"/>, max-only
    /// <see cref="Cardinality{TNode,TEdge,TInstanceData}"/> with band <c>[0,max]</c>, not a distinct
    /// rule type (spec story 54). Defaults to the single-instance limit <c>[0,1]</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is negative.</exception>
    public static Cardinality<TNode, TEdge, TInstanceData> InstanceLimit(NodeHandle prototype, int max = 1) =>
        new(Scope.Global, prototype, 0, max);

    /// <summary>The prototype this rule references, for handle-indexed evaluation (ADR 0005).</summary>
    IEnumerable<NodeHandle> IHandleReferencing.ReferencedHandles => [Prototype];

    /// <inheritdoc/>
    public IEnumerable<Violation> Check(SelectionView<TNode, TEdge, TInstanceData> view)
    {
        ArgumentNullException.ThrowIfNull(view);

        foreach (var band in Scope.Bands(view.ActiveRegions))
        {
            var count = view.Count(band.Matches, i => i.Prototype == Prototype);

            if (count < Min)
            {
                yield return new Violation(
                    ViolationKind.LowerBound,
                    $"Cardinality [{Min},{Max}] ({band.Description}): expected at least {Min} occurrence(s), found {count}.");
            }
            else if (count > Max)
            {
                yield return new Violation(
                    ViolationKind.UpperBound,
                    $"Cardinality [{Min},{Max}] ({band.Description}): expected at most {Max} occurrence(s), found {count}.");
            }
        }
    }
}
