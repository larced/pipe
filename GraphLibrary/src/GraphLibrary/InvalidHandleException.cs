namespace GraphLibrary;

/// <summary>
/// Thrown when a <see cref="NodeHandle"/> or <see cref="EdgeHandle"/> is used against a graph
/// it does not belong to (cross-graph misuse) or after its element has been removed (a stale
/// handle whose slot may since have been reused). The handle's internal id + generation/graph
/// stamp make this misuse detectable, so the graph fails fast here rather than silently reading
/// the wrong element or returning garbage (ADR 0003).
/// </summary>
public sealed class InvalidHandleException : Exception
{
    private InvalidHandleException(string message) : base(message)
    {
    }

    /// <summary>The handle was minted by a different graph (or is a default/uninitialised handle).</summary>
    internal static InvalidHandleException CrossGraph(string element) =>
        new($"This {element} handle was not minted by this graph; a handle is only valid against " +
            "the graph that created it.");

    /// <summary>The handle's element has been removed; its slot is free or reused by a newer element.</summary>
    internal static InvalidHandleException Stale(string element) =>
        new($"This {element} handle is stale: its {element} has been removed from the graph.");
}
