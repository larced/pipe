namespace GraphLibrary.Rules;

/// <summary>
/// One occurrence within a <see cref="Selection{TInstanceData}"/> of a <b>prototype</b> node,
/// carrying its own instance data and exactly one <see cref="Region"/> tag (CONTEXT.md → Instance /
/// Prototype, ADR 0005/0007). Multiple instances of the same prototype are distinct occurrences —
/// "two of this product" is two <see cref="Instance{TInstanceData}"/> values sharing a
/// <see cref="Prototype"/> but with different <see cref="Id"/>s.
/// </summary>
/// <remarks>
/// An occurrence carries no edges of its own beyond its prototype's, so <typeparamref name="TInstanceData"/>
/// deliberately does <b>not</b> include the graph's <c>TNode</c> payload (spec story 49): the overlay
/// stays free of base-graph payload and the base graph never churns. The prototype is referenced by
/// its opaque <see cref="NodeHandle"/> only; the base graph is supplied separately to the Evaluator
/// in a later ticket, so nothing here touches or mutates it.
/// </remarks>
/// <param name="Id">This occurrence's stable identity within its selection.</param>
/// <param name="Prototype">The node handle this instance is an occurrence of.</param>
/// <param name="Region">The single region tag partitioning this instance.</param>
/// <param name="Data">The caller-supplied per-occurrence data (never <c>TNode</c>).</param>
public sealed record Instance<TInstanceData>(
    InstanceId Id,
    NodeHandle Prototype,
    Region Region,
    TInstanceData Data);
