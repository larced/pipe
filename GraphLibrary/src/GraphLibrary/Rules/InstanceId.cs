namespace GraphLibrary.Rules;

/// <summary>
/// The stable, opaque identity of an <see cref="Instance{TInstanceData}"/> within a
/// <see cref="Selection{TInstanceData}"/>, returned by <see cref="Selection{TInstanceData}.Add"/> and
/// passed back to <see cref="Selection{TInstanceData}.Remove"/> (spec stories 46, 48). A plain,
/// allocation-free <see langword="readonly"/> <see langword="struct"/> usable as a dictionary key —
/// the selection-overlay counterpart of the graph's <see cref="NodeHandle"/> (ADR 0003 ethos).
/// </summary>
/// <remarks>
/// The value carries a per-selection instance number plus a selection stamp. The stamp is the
/// overlay's cross-selection guard: an id minted by one selection is rejected by another
/// (<see cref="ArgumentException"/>) rather than silently aliasing an unrelated instance that happens
/// to share the small integer number. A <see langword="default"/> id carries stamp 0, which no real
/// selection ever mints, so it never matches a live instance. Callers treat the value as opaque.
/// </remarks>
public readonly struct InstanceId : IEquatable<InstanceId>
{
    internal readonly int Value;
    internal readonly int SelectionId;

    internal InstanceId(int value, int selectionId)
    {
        Value = value;
        SelectionId = selectionId;
    }

    /// <inheritdoc/>
    public bool Equals(InstanceId other) => Value == other.Value && SelectionId == other.SelectionId;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InstanceId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Value, SelectionId);

    /// <summary>Value equality: same instance number in the same selection.</summary>
    public static bool operator ==(InstanceId left, InstanceId right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(InstanceId left, InstanceId right) => !left.Equals(right);
}
