namespace GraphLibrary.Rules;

/// <summary>
/// How a <see cref="Violation"/> fails, and the load-bearing distinction of the whole rule layer
/// (CONTEXT.md → Violation, ADR 0005): a shortfall the caller can still fix by adding
/// (<see cref="LowerBound"/>) versus an excess/conflict adding can only worsen
/// (<see cref="UpperBound"/>). This single classification is what lets <b>validity</b> and (in a
/// later ticket) <b>Availability</b> derive uniformly across built-in and custom rules — Availability
/// blocks a candidate only on newly-caused <see cref="UpperBound"/> breaches.
/// </summary>
public enum ViolationKind
{
    /// <summary>
    /// A lower-bound shortfall — "still needs X". The selection is short of a minimum and adding the
    /// right instance can resolve it (e.g. a <c>Cardinality</c> minimum not yet reached).
    /// </summary>
    LowerBound,

    /// <summary>
    /// An upper-bound or exclusion breach — "too many / conflicts". The selection exceeds a maximum or
    /// holds a forbidden combination; adding more can only preserve or worsen it, never fix it.
    /// </summary>
    UpperBound,
}
