namespace MeetingRooms.Api.Hubs;

/// <summary>
/// Sent to a room's viewers when one of its slots has been claimed.
/// </summary>
/// <remarks>
/// What this deliberately does not carry:
/// <list type="bullet">
/// <item>
/// <strong>Who booked it.</strong> <c>docs/requirements.md</c> §5 - a caller learns that a slot is
/// taken, and separately whether they are the one who took it. An event naming the booker would
/// broadcast to every viewer of the room exactly what the schedule endpoint refuses to disclose.
/// The consequence is accepted: a booker's <em>second</em> tab sees the slot turn booked but not
/// "booked by me" until it reads the schedule again.
/// </item>
/// <item>
/// <strong>A status flag.</strong> The event's name is the status. Bookings cannot be cancelled
/// (<c>docs/requirements.md</c> §4), so free-to-booked is the only transition there is, and an
/// <c>isBooked</c> that is always true is a field waiting to be read as though it could be false.
/// </item>
/// </list>
/// <para>
/// <see cref="RoomId"/> is carried even though the group already implies it: a client watching
/// more than one room receives every event through the same handler, and the SignalR client does
/// not say which subscription delivered it.
/// </para>
/// </remarks>
public sealed record SlotBookedEvent(int RoomId, int SlotId);
