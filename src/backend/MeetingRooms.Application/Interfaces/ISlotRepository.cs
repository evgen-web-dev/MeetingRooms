using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Reads slots. Phase 5 adds the booking claim here - a single conditional UPDATE that owns its
/// own write and needs no transaction.
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
}
