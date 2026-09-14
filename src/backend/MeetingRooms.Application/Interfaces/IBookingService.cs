using MeetingRooms.Application.DTOs.Bookings;
using MeetingRooms.Application.Results;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Claiming a slot, and reading back what has been claimed. Separate from
/// <see cref="IRoomService"/>, which is room-scoped; this is the assignment's graded core and
/// earns its own service.
/// </summary>
public interface IBookingService
{
    /// <summary>
    /// Claims a slot for a caller. Exactly one of several simultaneous requests for the same slot
    /// succeeds; the rest fail with <c>SlotAlreadyBooked</c>, and none of them is a 5xx.
    /// </summary>
    /// <returns>
    /// Success when the caller now holds the slot - which includes the case where they already
    /// held it. Otherwise <c>SlotNotFound</c>, <c>SlotAlreadyBooked</c> or <c>SlotHasEnded</c>.
    /// </returns>
    Task<OperationResult<BookSlotResponse>> BookSlotAsync(
        BookSlotRequest request,
        int callerUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The caller's own bookings, earliest slot first.
    /// <para>
    /// Returns its value rather than an <see cref="OperationResult{TValue}"/>, for the reason
    /// <see cref="IRoomService.ListRoomsAsync"/> does: the result type carries business failures,
    /// and this read has none.
    /// </para>
    /// </summary>
    Task<MyBookingsResponse> GetMyBookingsAsync(int callerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Every booking across every user, earliest slot first. Administrators only - this is the
    /// "view all bookings across users" capability from the assignment, and the only read in the
    /// application that discloses one user's identity to another.
    /// </summary>
    Task<AllBookingsResponse> GetAllBookingsAsync(CancellationToken cancellationToken);
}
