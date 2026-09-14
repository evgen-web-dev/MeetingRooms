namespace MeetingRooms.Application.Errors;

/// <summary>Business error codes for claiming a slot.</summary>
public static class BookingErrorCodes
{
    /// <summary>No slot has that id.</summary>
    public const string SlotNotFound = "SlotNotFound";

    /// <summary>
    /// The slot is already booked by someone else. This is the code every loser of a race
    /// receives, and the one <c>docs/requirements.md</c> §4 names as the conflict contract - so a
    /// client can branch on "someone got there first" without parsing a message.
    /// <para>
    /// A caller who already holds the slot does <em>not</em> get this code: that request succeeds.
    /// See <see cref="Results.SlotClaimOutcome.AlreadyClaimedByCaller"/>.
    /// </para>
    /// </summary>
    public const string SlotAlreadyBooked = "SlotAlreadyBooked";

    /// <summary>
    /// The slot's window has already closed. Note that a slot which has <em>started</em> but not
    /// finished is still bookable - half an hour of a meeting room is half an hour of a meeting
    /// room - so the rule is about <c>EndUtc</c>, not <c>StartUtc</c>.
    /// </summary>
    public const string SlotHasEnded = "SlotHasEnded";
}
