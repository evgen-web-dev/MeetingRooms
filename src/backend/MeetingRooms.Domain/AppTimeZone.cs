namespace MeetingRooms.Domain;

/// <summary>
/// The single zone every displayed time is expressed in, and the zone the slot grid's
/// 08:00-18:00 window is measured in - <c>docs/requirements.md</c> §7. There is no per-user and
/// no per-room zone.
/// </summary>
public static class AppTimeZone
{
    /// <summary>
    /// Published to clients once per schedule response, so the frontend renders with this value
    /// rather than holding a copy of it. Two copies is how a generator and a formatter drift.
    /// </summary>
    public const string IanaId = "Europe/Kyiv";

    /// <summary>
    /// The Windows registry name for the same zone. .NET 6 and later map IANA ids to Windows ids
    /// automatically, so this is a fallback for the case where that mapping is unavailable
    /// rather than a case anything is expected to hit.
    /// </summary>
    private const string WindowsId = "FLE Standard Time";

    /// <summary>
    /// Resolved once. <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> reads from the operating
    /// system on every call, so caching it keeps the grid generator free of I/O.
    /// </summary>
    public static TimeZoneInfo Instance { get; } = Resolve();

    private static TimeZoneInfo Resolve()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(IanaId, out var zone))
        {
            return zone;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(WindowsId, out zone))
        {
            return zone;
        }

        // Reached only when the host has no time-zone database at all - an image built without
        // tzdata, or one running with InvariantGlobalization enabled, where every lookup but UTC
        // fails. Naming both ids and the likely cause turns a startup crash into something
        // readable, because this runs before anything serves a request.
        throw new InvalidOperationException(
            $"Neither '{IanaId}' nor '{WindowsId}' could be resolved. The host is missing its "
            + "time-zone database, or the application is running with invariant globalization.");
    }
}
