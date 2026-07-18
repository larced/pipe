namespace GraphLibrary.Rules;

/// <summary>
/// A hypothetical addition the <see cref="Evaluator"/> is asked to weigh — one occurrence of a
/// <see cref="Prototype"/> node placed into a <see cref="Region"/> — for which
/// <see cref="Evaluator.Availability{TNode,TEdge,TInstanceData}"/> answers "may I add this next, and if
/// not why?" (CONTEXT.md → Availability, ADR 0005). It mirrors the shape of a
/// <see cref="Selection{TInstanceData}.Add"/> minus the per-occurrence instance data: availability is
/// derived from counts, regions and topology, so a candidate carries no <c>TInstanceData</c> — the
/// probe simulates the add with default instance data.
/// </summary>
/// <remarks>
/// The region is part of a candidate's identity because rules can be region-scoped: "add a wheel to the
/// FrontAxle" and "add a wheel to the RearAxle" are distinct questions that a per-region
/// <see cref="Cardinality{TNode,TEdge,TInstanceData}"/> answers differently. A caller building a
/// "what can I add" surface supplies the (prototype, region) pairs it offers; the engine never
/// enumerates the whole graph on its own.
/// </remarks>
/// <param name="Prototype">The node handle a hypothetical instance would be an occurrence of.</param>
/// <param name="Region">The region the hypothetical instance would be tagged with.</param>
public readonly record struct Candidate(NodeHandle Prototype, Region Region);
