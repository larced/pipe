namespace GraphLibrary;

/// <summary>
/// The stable, opaque identity of a Node (ADR 0003). A plain, non-generic,
/// allocation-free <see langword="readonly"/> <see langword="struct"/> usable as a
/// dictionary key, independent of the node's payload and unaffected by payload mutation.
/// </summary>
/// <remarks>
/// The value carries an internal slot index plus a generation and a graph stamp. Those
/// fields exist so a later ticket can add the cross-graph / stale-handle guard (ADR 0003)
/// without changing this type's shape; the minimal ticket-01 surface only relies on them
/// for identity. Callers treat the whole value as opaque — no field is public.
/// </remarks>
public readonly struct NodeHandle : IEquatable<NodeHandle>
{
    internal readonly int Index;
    internal readonly int Generation;
    internal readonly int GraphId;

    internal NodeHandle(int index, int generation, int graphId)
    {
        Index = index;
        Generation = generation;
        GraphId = graphId;
    }

    public bool Equals(NodeHandle other) =>
        Index == other.Index && Generation == other.Generation && GraphId == other.GraphId;

    public override bool Equals(object? obj) => obj is NodeHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, Generation, GraphId);

    public static bool operator ==(NodeHandle left, NodeHandle right) => left.Equals(right);

    public static bool operator !=(NodeHandle left, NodeHandle right) => !left.Equals(right);
}
