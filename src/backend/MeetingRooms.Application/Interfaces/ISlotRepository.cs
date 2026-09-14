using MeetingRooms.Application.Results;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Reads slots, and owns the one write that claims one.
/// <para>
/// <see cref="IUnitOfWork"/> is deliberately absent. <see cref="TryClaimAsync"/> is a single
/// statement that executes immediately and bypasses EF's change tracker, so there is no
/// <c>SaveChanges</c> to coordinate and no explicit transaction - which also keeps it clear of
/// <c>EnableRetryOnFailure</c>'s refusal to replay user-initiated transactions.
/// </para>
/// </summary>
public interface ISlotRepository
{
    /// <summary>
    /// One room's slots whose start falls in <c>[fromUtc, toUtc)</c>, ordered by start.
    /// <para>
    /// Half-open on purpose: a closed upper bound would return the slot beginning exactly at the
    /// boundary, so two adjacent ranges would each claim it.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Slot>> ListForRoomAsync(
        int roomId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims a slot for a user, and reports what happened.
    /// <para>
    /// <strong>This is the only code in the application permitted to write
    /// <see cref="Slot.BookedByUserId"/>.</strong> The no-double-booking invariant lives in this
    /// one statement's <c>WHERE</c> clause rather than in a standing database constraint, so a
    /// second write path would void it silently. The trade is accepted and stated in the README
    /// (<c>docs/decisions.md</c>).
    /// </para>
    /// </summary>
    /// <param name="nowUtc">
    /// Read once by the caller and used both to decide whether the window has closed and to stamp
    /// the booking, so the statement that refuses a claim and the explanation given for it cannot
    /// disagree. Expected truncated to whole seconds: the parameter inherits the column's
    /// <c>datetime2(0)</c> type and is therefore <em>rounded</em> on the way to the server, so an
    /// untruncated value could judge a slot ended up to half a second early - and would be echoed
    /// to the client that far from what is stored.
    /// </param>
    Task<SlotClaimOutcome> TryClaimAsync(
        int slotId,
        int userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// One user's booked slots, earliest first, each with its room loaded.
    /// <para>
    /// Scoped in the query rather than filtered after fetching, so no caller can forget to narrow
    /// it. Unbounded by design: it grows with use and has no natural window. At this scale that is
    /// accepted rather than paged - see <c>docs/decisions.md</c>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Slot>> ListBookedForUserAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Every booked slot across every user, earliest first, each with its room and its booker
    /// loaded. Administrator-facing; the caller is responsible for having checked the role.
    /// </summary>
    Task<IReadOnlyList<Slot>> ListAllBookedAsync(CancellationToken cancellationToken);
}
