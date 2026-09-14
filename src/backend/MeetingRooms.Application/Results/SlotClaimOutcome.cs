namespace MeetingRooms.Application.Results;

/// <summary>
/// What a claim attempt actually did.
/// <para>
/// An enum rather than a <c>bool</c> because zero rows affected has four possible explanations,
/// and rather than an <see cref="OperationResult"/> because a port reports what happened -
/// deciding which of these is a business failure is the service's job, not the repository's. The
/// same split <see cref="RoomDeleteOutcome"/> already uses.
/// </para>
/// </summary>
public enum SlotClaimOutcome
{
    /// <summary>
    /// First deliberately, so that <c>default(SlotClaimOutcome)</c> is not a success case. An
    /// uninitialised value should never read as "the slot was booked".
    /// </summary>
    NotFound,

    /// <summary>The conditional UPDATE matched one row and wrote it. The caller booked the slot.</summary>
    Claimed,

    /// <summary>
    /// The row is already booked, and the booker is the caller. Reported as success, not as a
    /// conflict.
    /// <para>
    /// This exists because <c>EnableRetryOnFailure</c> replays an operation when a transient fault
    /// lands after the commit but before the acknowledgement. The replayed conditional UPDATE then
    /// finds the slot booked by its own winning write, matches zero rows, and would otherwise tell
    /// the caller who actually won that they lost. The invariant holds either way - nothing
    /// double-books - but the response would lie to the one caller entitled to a truthful yes.
    /// </para>
    /// <para>
    /// The accepted consequence is that booking is <em>idempotent per user</em>: a deliberate
    /// second request from the same account also succeeds, because nothing distinguishes it from a
    /// replay without an idempotency key. See <c>docs/decisions.md</c>.
    /// </para>
    /// </summary>
    AlreadyClaimedByCaller,

    /// <summary>The row is already booked, by somebody else. The conflict the assignment is about.</summary>
    AlreadyBooked,

    /// <summary>The row exists and is free, but its window has closed.</summary>
    HasEnded
}
