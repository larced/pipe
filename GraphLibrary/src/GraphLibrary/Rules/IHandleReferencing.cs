namespace GraphLibrary.Rules;

/// <summary>
/// An optional capability a built-in <see cref="IRule{TNode,TEdge,TInstanceData}"/> implements to
/// declare the prototype <see cref="NodeHandle"/>s it references (CONTEXT.md → Evaluator, ADR 0005).
/// It lets the Evaluator index rules by referenced handle (<see cref="RuleIndex{TNode,TEdge,TInstanceData}"/>)
/// so a later Availability derivation — "does adding a candidate of prototype P newly break anything?"
/// — evaluates only the rules that reference P, never the whole rule set per candidate.
/// </summary>
/// <remarks>
/// <para>
/// The capability is <b>opt-in and never affects correctness</b>: a rule that does not implement it
/// (a custom <see cref="IRule{TNode,TEdge,TInstanceData}"/>, or a built-in like
/// <c>RequiresEdge</c> whose relevant handles are discovered from the base graph rather than fixed at
/// construction) is treated as referencing <em>every</em> handle and is always evaluated. So the index
/// only ever <em>prunes</em> rules it can prove irrelevant; it never skips a rule it is unsure about.
/// </para>
/// <para>
/// It is deliberately non-generic — a handle carries no payload type — so a rule generic over
/// <c>TNode, TEdge, TInstanceData</c> implements it with a plain
/// <see cref="ReferencedHandles"/> property, usually as an explicit interface implementation to keep
/// it off the rule's own vocabulary surface.
/// </para>
/// </remarks>
public interface IHandleReferencing
{
    /// <summary>
    /// The prototype handles whose presence can change this rule's verdict. A rule declares these so
    /// the Evaluator can skip it when evaluating a candidate that touches none of them; a rule that
    /// cannot pin its relevance to specific handles does not implement this interface at all.
    /// </summary>
    IEnumerable<NodeHandle> ReferencedHandles { get; }
}
