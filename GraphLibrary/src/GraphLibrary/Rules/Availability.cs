namespace GraphLibrary.Rules;

/// <summary>
/// The <see cref="Evaluator"/>-derived answer, per <see cref="Candidate"/>, of whether adding it keeps
/// the selection valid — the "why-not" preview (CONTEXT.md → Availability, ADR 0005). It is one of two
/// shapes: <see cref="Available"/> (with headroom) when the candidate may be added, or
/// <see cref="Blocked"/> (with reasons) when it may not. Both are produced by
/// <see cref="Evaluator.Availability{TNode,TEdge,TInstanceData}"/>; a caller pattern-matches on the two
/// cases to render its surface.
/// </summary>
/// <remarks>
/// Availability is <b>engine-derived, not per-rule-implemented</b>: the engine simulates adding the
/// candidate, re-runs <see cref="Evaluator.Check{TNode,TEdge,TInstanceData}"/>, and blocks only on
/// <em>newly-caused</em> upper-bound / exclusion breaches — so "why not available" works for arbitrary
/// custom rules with no extra author code. It unions that derivation with any <see cref="GateFailure"/>
/// from opt-in <see cref="Gate"/>s (ADR 0005: <c>Availability = derived-blocks ∪ gate-failures</c>).
/// </remarks>
public abstract record Availability
{
    // Sealed hierarchy: the only two subtypes are the nested Available / Blocked below. The base
    // constructor is private protected so no other assembly can add a third case, keeping a caller's
    // switch exhaustive over exactly Available and Blocked.
    private protected Availability(Candidate candidate) => Candidate = candidate;

    /// <summary>The candidate this answer is about.</summary>
    public Candidate Candidate { get; }

    /// <summary>
    /// The candidate may be added: doing so causes no new upper-bound / exclusion breach and no gate
    /// forbids it (CONTEXT.md → Availability).
    /// </summary>
    /// <param name="Candidate">The candidate that is available.</param>
    /// <param name="Headroom">
    /// How many occurrences of the candidate can still be added before an upper bound would block the
    /// next one (always at least 1 for an available candidate), or <see langword="null"/> when no upper
    /// bound constrains it within a probe ceiling — i.e. the room is unbounded or larger than the ceiling,
    /// which for a why-not preview reads the same as "no practical limit". It is derived by probing, so it
    /// assumes the constraining rules are monotonic in count — true of every built-in; a pathological
    /// custom rule may report a best-effort figure, though the Available/Blocked verdict for the immediate
    /// add is always exact.
    /// </param>
    public sealed record Available(Candidate Candidate, int? Headroom) : Availability(Candidate);

    /// <summary>
    /// The candidate may not be added. The two block mechanisms surface together (ADR 0005:
    /// <c>Availability = derived-blocks ∪ gate-failures</c>); the candidate is blocked when either list
    /// is non-empty.
    /// </summary>
    /// <param name="Candidate">The candidate that is blocked.</param>
    /// <param name="Breaches">
    /// The newly-caused upper-bound / exclusion <see cref="Violation"/>s adding the candidate would
    /// introduce — attributed by violation <em>identity</em>, so pushing an already-full cardinality
    /// further registers here as a fresh breach even though a breach already existed.
    /// </param>
    /// <param name="GateFailures">The unmet <see cref="Gate"/> preconditions forbidding the candidate.</param>
    public sealed record Blocked(
        Candidate Candidate,
        IReadOnlyList<Violation> Breaches,
        IReadOnlyList<GateFailure> GateFailures) : Availability(Candidate);
}
