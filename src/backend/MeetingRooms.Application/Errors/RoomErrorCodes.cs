namespace MeetingRooms.Application.Errors;

/// <summary>Business error codes for rooms and their schedules.</summary>
public static class RoomErrorCodes
{
    /// <summary>
    /// No room has that id. Also the answer when a schedule is requested for one, so that a
    /// missing room and an empty grid are never confused.
    /// </summary>
    public const string RoomNotFound = "RoomNotFound";

    /// <summary>
    /// A delete was refused because at least one of the room's slots is booked. Refused rather
    /// than cascaded: bookings cannot be cancelled, rescheduled or modified
    /// (<c>docs/requirements.md</c> §4), so removing the room out from under one would be a
    /// cancellation by another name - and a silent one.
    /// </summary>
    public const string RoomHasBookedSlots = "RoomHasBookedSlots";
}
