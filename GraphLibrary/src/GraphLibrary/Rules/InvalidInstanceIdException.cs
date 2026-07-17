namespace GraphLibrary.Rules;

/// <summary>
/// Thrown when an <see cref="InstanceId"/> is used against a <see cref="Selection{TInstanceData}"/>
/// that did not mint it (cross-selection misuse). The id's internal selection stamp makes this
/// detectable, so the selection fails fast here rather than silently aliasing an unrelated instance
/// that happens to share the same small integer number — the overlay counterpart of the graph's
/// cross-graph handle guard (<see cref="InvalidHandleException"/>, ADR 0003).
/// </summary>
/// <remarks>
/// This covers only cross-selection <em>misuse</em>, which is a programmer bug. A <em>stale</em> id —
/// one this selection did mint but whose instance has since been removed — is <b>not</b> an error: it
/// makes <see cref="Selection{TInstanceData}.Remove"/> return <see langword="false"/> (idempotent) and
/// <see cref="Selection{TInstanceData}.TryGet"/> report absence.
/// </remarks>
public sealed class InvalidInstanceIdException : Exception
{
    private InvalidInstanceIdException(string message) : base(message)
    {
    }

    /// <summary>The id was minted by a different selection (or is a default/uninitialised id).</summary>
    internal static InvalidInstanceIdException CrossSelection() =>
        new("This instance id was not minted by this selection; an id is only valid against the " +
            "selection that created it.");
}
