namespace GraphLibrary;

/// <summary>
/// A single payload-replacement event broadcast on a <see cref="Graph{TNode,TEdge}"/>'s observable
/// change channel (ADR 0002/0003): the <see cref="Handle"/> whose payload was replaced, the
/// <see cref="OldPayload"/> it carried, and the <see cref="NewPayload"/> it now carries.
/// </summary>
/// <remarks>
/// Both keys a secondary index needs are present in one value: <see cref="OldPayload"/> to retire
/// the stale content key and <see cref="NewPayload"/> to register the fresh one, both mapping to the
/// unchanged <see cref="Handle"/>. A plain, allocation-free <see langword="readonly"/>
/// <see langword="struct"/> — the channel stays cheap when no one derives content indices from it.
/// <typeparamref name="THandle"/> is <see cref="NodeHandle"/> or <see cref="EdgeHandle"/>;
/// <typeparamref name="TPayload"/> is the corresponding <c>TNode</c> or <c>TEdge</c>.
/// </remarks>
/// <param name="Handle">The handle whose payload was replaced. Unaffected by the replacement (ADR 0003).</param>
/// <param name="OldPayload">The payload the element carried before the replacement.</param>
/// <param name="NewPayload">The payload the element carries after the replacement.</param>
public readonly record struct PayloadChange<THandle, TPayload>(
    THandle Handle,
    TPayload OldPayload,
    TPayload NewPayload);
