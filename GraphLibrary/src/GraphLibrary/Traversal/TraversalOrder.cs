namespace GraphLibrary.Traversal;

/// <summary>
/// The order a <see cref="GraphTraversal.Traverse{TNode,TEdge}(IReadableGraph{TNode,TEdge},NodeHandle,TraversalOrder)"/>
/// walk expands Nodes in (CONTEXT.md → Traversal, "a chosen order", spec story 28). Both orders share
/// the same visitor steering and the same visited-set termination; they differ only in frontier shape.
/// </summary>
public enum TraversalOrder
{
    /// <summary>
    /// Pre-order depth-first: a Node is produced before its out-neighbours, and a whole branch is
    /// exhausted before the next sibling. The default — the cheapest frontier (a stack) and the order
    /// most reachability questions want.
    /// </summary>
    DepthFirst = 0,

    /// <summary>
    /// Breadth-first: Nodes are produced in non-decreasing edge-distance from the start, one frontier
    /// layer at a time. The order an unweighted shortest walk builds on.
    /// </summary>
    BreadthFirst,
}
