namespace MeetingRooms.Application.DTOs.Bookings;

/// <summary>
/// A booking request in full: one server-generated slot id, and nothing else.
/// </summary>
/// <remarks>
/// The client never sends a time. That is what makes the booking key immune to clock skew, to a
/// time picker glitch, and to <c>datetime2</c> precision - no client-supplied value participates
/// in booking identity, so nothing a caller can send produces two rows meaning one slot. It is
/// also why <c>Slot</c>'s key is a surrogate rather than <c>(RoomId, StartUtc)</c>.
/// </remarks>
public sealed record BookSlotRequest(int SlotId);

/// <param name="BookedAtUtc">
/// When the claim was written, as stored. On a replayed request this is the <em>original</em>
/// claim's timestamp rather than the replay's clock - the booking did not happen twice, and
/// reporting the later time would be a quieter version of the misreport the replay path exists to
/// avoid.
/// </param>
public sealed record BookSlotResponse(int SlotId, DateTime BookedAtUtc);

/// <summary>One of the caller's own bookings.</summary>
/// <remarks>
/// Carries no booker identity because the caller is the booker. The room's name is flattened in
/// rather than exposing a nested room object: a list row renders as one line.
/// </remarks>
public sealed record MyBookingResponse(
    int SlotId,
    int RoomId,
    string RoomName,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime BookedAtUtc);

/// <summary>One booking as an administrator sees it.</summary>
/// <param name="BookedByEmail">
/// The only place in this API where one user's identity is disclosed to another, and it is
/// administrator-only by <c>docs/requirements.md</c> §5. An ordinary caller learns that a slot is
/// taken, and separately whether they are the one who took it.
/// </param>
public sealed record AdminBookingResponse(
    int SlotId,
    int RoomId,
    string RoomName,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime BookedAtUtc,
    string BookedByEmail);

/// <param name="TimeZoneId">
/// Named once per response, exactly as <c>ScheduleResponse</c> does it, so a client renders with
/// this value instead of holding its own copy of the zone. A "my bookings" screen may never call
/// the schedule endpoint, and two copies of a constant is how a generator and a formatter drift
/// apart.
/// </param>
public sealed record MyBookingsResponse(string TimeZoneId, IReadOnlyList<MyBookingResponse> Bookings);

/// <inheritdoc cref="MyBookingsResponse"/>
public sealed record AllBookingsResponse(string TimeZoneId, IReadOnlyList<AdminBookingResponse> Bookings);
