namespace MeetingRooms.Domain.Entities;

/// <summary>
/// One fixed, bookable window on one room.
/// <para>
/// Slots are rows rather than a concept computed at read time, and that is the decision the
/// whole concurrency design rests on: one slot is exactly one row, identified by a
/// server-generated key, so phase 5's booking claim can be a single conditional UPDATE whose
/// outcome the database decides under a row lock. A client never sends a time, so no clock skew
/// and no <c>datetime2</c> precision can produce two rows meaning the same slot.
/// </para>
/// <para>
/// There is no separate booking entity. A booking here has no state of its own - no
/// cancellation, no rescheduling, no attendees - so it would be a 1:1 table whose only effect is
/// a join. See <c>docs/decisions.md</c>.
/// </para>
/// </summary>
public sealed class Slot
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public Room Room { get; set; } = null!;

    /// <summary>
    /// Start of the window, UTC. The wall-clock hour it stands for is fixed in the display zone
    /// (<c>docs/requirements.md</c> §7), so the same 08:00 slot is a different instant either
    /// side of a daylight-saving boundary.
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// End of the window, UTC, and exclusive: a slot covers <c>[StartUtc, EndUtc)</c>. Two
    /// back-to-back slots therefore do not overlap, which is why the grid can be contiguous.
    /// </summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// The booker, or <c>null</c> while the slot is free - a slot has no third state.
    /// <para>
    /// Phase 5's <c>TryClaimAsync</c> is the only code permitted to write this column. The
    /// no-double-booking invariant lives in that one statement's <c>WHERE</c> clause rather than
    /// in a standing database constraint, so a second write path would void it silently
    /// (<c>docs/decisions.md</c>).
    /// </para>
    /// </summary>
    public int? BookedByUserId { get; set; }

    public AppUser? BookedByUser { get; set; }

    /// <summary>When the claim succeeded, UTC. <c>null</c> while the slot is free.</summary>
    public DateTime? BookedAtUtc { get; set; }
}
