namespace GraphLibrary.Rules;

/// <summary>
/// One unmet <see cref="Gate"/> precondition that makes a <see cref="Candidate"/> unavailable
/// (CONTEXT.md → Gating, ADR 0005): the <see cref="Gated"/> prototype cannot be added yet because a
/// precondition over the <em>current</em> selection does not hold. It is the strict counterpart of a
/// derived <see cref="Violation"/>: a gate failure is reported by
/// <see cref="Evaluator.Availability{TNode,TEdge,TInstanceData}"/> (never by
/// <see cref="Evaluator.Check{TNode,TEdge,TInstanceData}"/>), because a gate governs <em>when</em> a
/// node may be added, not whether the selection is valid.
/// </summary>
/// <param name="Gated">The prototype the failing gate guards.</param>
/// <param name="Requirement">A human-readable description of the precondition that is not yet met.</param>
public sealed record GateFailure(NodeHandle Gated, string Requirement);
