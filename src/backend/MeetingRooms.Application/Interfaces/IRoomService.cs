using MeetingRooms.Application.DTOs.Rooms;
using MeetingRooms.Application.Results;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Rooms and their schedules. One service rather than two, because every operation here is
/// room-scoped; phase 5's booking write gets its own.
/// </summary>
public interface IRoomService
{
    /// <summary>
    /// Every room, ordered by name.
    /// <para>
    /// Returns the list rather than an <see cref="OperationResult{TValue}"/> on purpose. The
    /// result type exists to carry business failures, and this read has none - wrapping it would
    /// make every caller branch on a path that cannot happen.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RoomResponse>> ListRoomsAsync(CancellationToken cancellationToken);

    Task<OperationResult<RoomResponse>> GetRoomAsync(int roomId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the room and generates its grid for the current window in the same write, so a
    /// room is never briefly visible with an empty schedule.
    /// </summary>
    Task<OperationResult<RoomResponse>> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken);

    Task<OperationResult<RoomResponse>> UpdateRoomAsync(int roomId, UpdateRoomRequest request, CancellationToken cancellationToken);

    Task<OperationResult> DeleteRoomAsync(int roomId, CancellationToken cancellationToken);

    /// <param name="callerUserId">
    /// Decides <see cref="ScheduleSlotResponse.IsBookedByMe"/>, and nothing else. No booker
    /// identity is disclosed to anyone.
    /// </param>
    Task<OperationResult<ScheduleResponse>> GetScheduleAsync(
        int roomId,
        ScheduleRangeRequest range,
        int callerUserId,
        CancellationToken cancellationToken);
}
