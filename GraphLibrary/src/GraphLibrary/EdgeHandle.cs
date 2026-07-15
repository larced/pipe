namespace GraphLibrary;

/// <summary>
/// The stable, opaque identity of an Edge (ADR 0001/0003). Edges are first-class and
/// independently identified, so two nodes may be joined by many edges; each carries its own
/// handle. A plain, non-generic, allocation-free <see langword="readonly"/>
/// <see langword="struct"/> usable as a dictionary key, independent of the edge's payload.
/// </summary>
/// <remarks>
/// Shape mirrors <see cref="NodeHandle"/>: an internal slot index plus generation and graph
/// stamps reserved for the later cross-graph / stale-handle guard (ADR 0003). Opaque to callers.
/// </remarks>
public readonly struct EdgeHandle : IEquatable<EdgeHandle>
{
    internal readonly int Index;
    internal readonly int Generation;
    internal readonly int GraphId;

    internal EdgeHandle(int index, int generation, int graphId)
    {
        Index = index;
        Generation = generation;
        GraphId = graphId;
    }

    public bool Equals(EdgeHandle other) =>
        Index == other.Index && Generation == other.Generation && GraphId == other.GraphId;

    public override bool Equals(object? obj) => obj is EdgeHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, Generation, GraphId);

    public static bool operator ==(EdgeHandle left, EdgeHandle right) => left.Equals(right);

    public static bool operator !=(EdgeHandle left, EdgeHandle right) => !left.Equals(right);
}
