namespace MeetingRooms.Application.DTOs.Rooms;

public sealed record RoomResponse(int Id, string Name, int Capacity);

public sealed record CreateRoomRequest(string Name, int Capacity);

/// <summary>
/// Identical in shape to <see cref="CreateRoomRequest"/> and deliberately not the same type. The
/// validation filter resolves validators by <em>concrete</em> type, so one shared request type
/// would mean one shared rule set for two operations that are free to diverge - and the first
/// time they do, the shared type is the thing that has to be untangled.
/// </summary>
public sealed record UpdateRoomRequest(string Name, int Capacity);

/// <summary>
/// The window a schedule is asked for.
/// </summary>
/// <param name="FromUtc">
/// Inclusive lower bound. Absent means the start of the current day in the display zone.
/// </param>
/// <param name="ToUtc">
/// Exclusive upper bound. Absent means the end of the horizon.
/// </param>
/// <remarks>
/// <see cref="DateTimeOffset"/> rather than <see cref="DateTime"/>. MVC parses a query-string
/// <c>DateTime</c> in an <c>AdjustToUniversal</c> style: a trailing <c>Z</c> and an explicit
/// offset both arrive as <c>Kind.Utc</c>, while a value carrying neither arrives as
/// <c>Unspecified</c> and is taken at face value - so the service would have to normalise two
/// kinds, silently and wrongly if it missed one. <see cref="DateTimeOffset"/> binds all three
/// forms unambiguously and <c>.UtcDateTime</c> needs no normalising.
/// <para>
/// One trap survives either choice: a value sent with no offset at all is interpreted in the
/// <em>server's</em> local zone, which is not the same in the dev container as on App Service.
/// See <c>docs/decisions.md</c>; clients send <c>Z</c>.
/// </para>
/// </remarks>
public sealed record ScheduleRangeRequest(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);

/// <param name="TimeZoneId">
/// The IANA zone the grid's working day is measured in, so a client renders with this value
/// instead of holding its own copy. Two copies is how a generator and a formatter drift apart.
/// Sent once per response rather than per slot: the instant plus the zone already determines the
/// offset, and a per-slot offset would be derived data able to disagree with itself.
/// </param>
public sealed record ScheduleResponse(
    int RoomId,
    string RoomName,
    string TimeZoneId,
    IReadOnlyList<ScheduleSlotResponse> Slots);

/// <summary>
/// One slot as a caller may see it.
/// </summary>
/// <param name="IsBookedByMe">
/// The only personal fact on the wire. Who booked a slot is never disclosed
/// (<c>docs/requirements.md</c> §5) - a caller learns that a slot is taken, and separately
/// whether they are the one who took it.
/// </param>
public sealed record ScheduleSlotResponse(
    int Id,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsBooked,
    bool IsBookedByMe);
