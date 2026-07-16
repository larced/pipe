namespace GraphLibrary.Traversal;

/// <summary>
/// The instruction a <see cref="GraphTraversal.Traverse{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,System.Func{NodeHandle,VisitControl},TraversalOrder)"/>
/// visitor returns for each Node it is shown, steering the walk (CONTEXT.md → Traversal, spec story 29).
/// The visited Node is produced in every case; what differs is what happens <em>after</em> it.
/// </summary>
public enum VisitControl
{
    /// <summary>Produce this Node and descend into its out-neighbours — the ordinary case.</summary>
    Continue = 0,

    /// <summary>
    /// Produce this Node but prune its subtree: its out-neighbours are not walked (unless reached by
    /// another route). Lets a caller cut off a branch without halting the whole traversal.
    /// </summary>
    SkipDescendants,

    /// <summary>
    /// Produce this Node and then halt the entire traversal — no further Nodes follow. The precise
    /// "stop here" that story 29 asks for; the triggering Node is inclusive.
    /// </summary>
    Stop,
}
