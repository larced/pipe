namespace GraphLibrary.Rules;

/// <summary>
/// The flat, opaque grouping label an <see cref="Instance{TInstanceData}"/> carries, partitioning a
/// <see cref="Selection{TInstanceData}"/> for validation (CONTEXT.md → Region, ADR 0007). A plain
/// value with equality — the same ethos as a <see cref="NodeHandle"/> (ADR 0003): callers construct
/// one from their own label and treat it opaquely.
/// </summary>
/// <remarks>
/// <para>
/// A region is <b>flat</b> (no hierarchy), <b>abstract</b> (it carries no base-graph topology, so
/// partitioning a selection costs the base graph nothing), and has <b>no registry</b> — the engine
/// owns no set of known regions; a region <em>exists iff some instance carries it</em>
/// (<see cref="Selection{TInstanceData}.ActiveRegions"/>) and springs into being on first use. A
/// closed set of regions, or "≤ N active regions", is expressed later as an opt-in rule, never as a
/// library-imposed schema.
/// </para>
/// <para>
/// Backed by a caller-supplied <see cref="Label"/> with ordinal (case-sensitive) equality. An app
/// wanting roll-up-like grouping encodes a path in the label ("Vehicle/FrontAxle") and names sets by
/// convention — the engine stays free of any tree (ADR 0007).
/// </para>
/// </remarks>
public readonly struct Region : IEquatable<Region>
{
    private readonly string _label;

    /// <summary>
    /// Creates the region identified by <paramref name="label"/>. The label is the region's whole
    /// identity, so it must be non-empty; any non-empty value is legal (regions are dynamic, never
    /// pre-declared). A <see langword="default"/> <see cref="Region"/> — one never given a label —
    /// is not a usable region and is rejected by <see cref="Selection{TInstanceData}.Add"/>.
    /// </summary>
    public Region(string label)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        _label = label;
    }

    /// <summary>
    /// The caller-supplied label backing this region, or <see langword="null"/> for a
    /// <see langword="default"/> region. Exposed so an app can read back the path it encoded for
    /// hierarchy-by-convention; the engine itself treats the whole value opaquely.
    /// </summary>
    public string? Label => _label;

    /// <inheritdoc/>
    public bool Equals(Region other) => string.Equals(_label, other._label, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Region other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        _label is null ? 0 : StringComparer.Ordinal.GetHashCode(_label);

    /// <summary>Value equality by <see cref="Label"/> (ordinal).</summary>
    public static bool operator ==(Region left, Region right) => left.Equals(right);

    /// <summary>Value inequality by <see cref="Label"/> (ordinal).</summary>
    public static bool operator !=(Region left, Region right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => _label ?? "<default region>";
}
