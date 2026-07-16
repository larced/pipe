namespace GraphLibrary;

/// <summary>
/// The opt-in, off-by-default topology Validators a <see cref="Graph{TNode,TEdge}"/> can be
/// asked to enforce (CONTEXT.md → Validator, ADR 0001, spec stories 16–19). The core structure
/// holds any topology unconditionally; enabling a validator is what makes a <em>given</em> graph
/// refuse the shape it names, checked at mutation time. This is validation <em>policy</em>, kept
/// deliberately separate from the store's structural <em>capability</em>.
/// </summary>
/// <remarks>
/// A <see cref="FlagsAttribute"/> enum so the three validators are independently composable — a
/// consumer ORs together exactly the ones they want and pays for no others (story 19). The default,
/// <see cref="None"/>, is the permissive core: cycles, self-loops, and parallel edges all accepted.
/// </remarks>
[Flags]
public enum TopologyValidator
{
    /// <summary>No validator: every topology is accepted (the permissive default).</summary>
    None = 0,

    /// <summary>
    /// Reject any edge whose addition would introduce a cycle — including a self-loop, which is a
    /// cycle of length one. Keeps the graph acyclic (story 16).
    /// </summary>
    Acyclic = 1 << 0,

    /// <summary>
    /// Reject an edge between an ordered endpoint pair that is already connected, so at most one
    /// edge joins a given source to a given target (story 17). Direction matters: <c>a→b</c> and
    /// <c>b→a</c> are distinct pairs, and a self-loop is a pair with no parallel unless repeated.
    /// </summary>
    SimpleGraph = 1 << 1,

    /// <summary>Reject an edge from a node to itself (story 18).</summary>
    NoSelfLoops = 1 << 2,
}
