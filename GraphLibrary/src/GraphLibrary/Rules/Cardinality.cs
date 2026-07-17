namespace GraphLibrary.Rules;

/// <summary>
/// The built-in rule "the selection must hold between <see cref="Min"/> and <see cref="Max"/>
/// occurrences of a given <see cref="Prototype"/>, within a region <see cref="Scope"/>" (CONTEXT.md →
/// Rule / Scope, ADR 0007). One built-in absorbs the prototype's separate per-region cardinality and
/// instance-limit rules: a single-instance limit is a <see cref="Cardinality{TNode,TEdge,TInstanceData}"/>
/// at <see cref="Scope.Global"/> with <c>[0,1]</c> (spec story 54, exposed as
/// <see cref="InstanceLimit"/>).
/// </summary>
/// <remarks>
/// Its <see cref="Check"/> is the canonical use of the one primitive — <see cref="Scope"/> supplies
/// the region predicate, the prototype supplies the <c>among</c> narrowing, and the view counts. A
/// count below <see cref="Min"/> is a <see cref="ViolationKind.LowerBound"/> shortfall ("still needs
/// more"); a count above <see cref="Max"/> is a <see cref="ViolationKind.UpperBound"/> breach ("too
/// many"). This tracer slice ships the rule at <see cref="Scope.Global"/>; the region-targeting scopes
/// are additive with no change here.
/// </remarks>
public sealed class Cardinality<TNode, TEdge, TInstanceData> : IRule<TNode, TEdge, TInstanceData>
{
    /// <summary>The region reach counted (this slice: <see cref="Scope.Global"/>).</summary>
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
    /// A single-instance limit: at most one occurrence of <paramref name="prototype"/> across the
    /// whole selection — a <see cref="Cardinality{TNode,TEdge,TInstanceData}"/> at
    /// <see cref="Scope.Global"/> with band <c>[0,1]</c> (spec story 54).
    /// </summary>
    public static Cardinality<TNode, TEdge, TInstanceData> InstanceLimit(NodeHandle prototype) =>
        new(Scope.Global, prototype, 0, 1);

    /// <inheritdoc/>
    public IEnumerable<Violation> Check(SelectionView<TNode, TEdge, TInstanceData> view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var count = view.Count(Scope.Matches, i => i.Prototype == Prototype);

        if (count < Min)
        {
            yield return new Violation(
                ViolationKind.LowerBound,
                $"Cardinality [{Min},{Max}]: expected at least {Min} occurrence(s), found {count}.");
        }
        else if (count > Max)
        {
            yield return new Violation(
                ViolationKind.UpperBound,
                $"Cardinality [{Min},{Max}]: expected at most {Max} occurrence(s), found {count}.");
        }
    }
}
