namespace MeetingRooms.Application.Results;

/// <summary>
/// What a delete attempt actually did.
/// <para>
/// An enum rather than a <c>bool</c> because the caller has to tell 404 from 409, and rather than
/// an <see cref="OperationResult"/> because a port reports what happened - deciding that
/// <see cref="HasBookedSlots"/> is a business failure is the service's job, not the
/// repository's.
/// </para>
/// </summary>
public enum RoomDeleteOutcome
{
    /// <summary>
    /// First deliberately, so that <c>default(RoomDeleteOutcome)</c> is not the success case. An
    /// uninitialised value should never read as "the room was deleted".
    /// </summary>
    NotFound,

    Deleted,

    HasBookedSlots
}
