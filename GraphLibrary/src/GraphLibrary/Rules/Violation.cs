namespace GraphLibrary.Rules;

/// <summary>
/// A single finding a <see cref="IRule{TNode,TEdge,TInstanceData}"/> reports about a Selection
/// (CONTEXT.md → Violation, ADR 0005), carrying its <see cref="ViolationKind"/> classification and a
/// human-readable <see cref="Message"/>. <see cref="Evaluator.Check{TNode,TEdge,TInstanceData}"/>
/// returns the flat union of every rule's violations; a selection is valid exactly when that union is
/// empty.
/// </summary>
/// <remarks>
/// The <see cref="Kind"/> classification — not the message — is the machine-readable part: it is what
/// a later ticket's Availability derivation keys on (blocking a candidate on newly-caused
/// <see cref="ViolationKind.UpperBound"/> breaches). The <see cref="Message"/> is for a "why not valid"
/// report and carries no contract.
/// </remarks>
/// <param name="Kind">Whether this is a lower-bound shortfall or an upper-bound / exclusion breach.</param>
/// <param name="Message">A human-readable description of the finding, for a validity report.</param>
public sealed record Violation(ViolationKind Kind, string Message);
