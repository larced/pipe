namespace GraphLibrary;

/// <summary>
/// Thrown when a payload-derived key collides inside a <em>unique</em>
/// <see cref="SecondaryIndex{TKey}"/> — two distinct nodes would map to the same key. Raised in two
/// places, both programmer bugs the fluent builder's uniqueness contract catches (ADR 0006): during
/// keyed construction (<see cref="GraphBuilder{TNode,TEdge,TKey}.Build()"/>) when two payloads share
/// a key, and later, when a <c>SetPayload</c> would re-key a node onto a key another node already
/// holds. Distinct from <see cref="InvalidHandleException"/> (handle misuse) and
/// <see cref="ValidatorRejectedException"/> (a rejected topology): this is a key-uniqueness breach.
/// </summary>
public sealed class DuplicateKeyException : Exception
{
    private DuplicateKeyException(string message) : base(message)
    {
    }

    // The key value is interpolated so the caller can see which key collided; the key type is not
    // carried on the exception surface (SecondaryIndex stays single-parameter in TKey, ADR 0003).
    internal static DuplicateKeyException ForKey(object key) =>
        new($"The key '{key}' is already held by another node; a unique keyed index requires each "
            + "node's key to be distinct.");
}
