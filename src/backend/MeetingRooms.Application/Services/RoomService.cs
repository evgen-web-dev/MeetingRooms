using MeetingRooms.Application.DTOs.Rooms;
using MeetingRooms.Application.Errors;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using MeetingRooms.Domain;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Application.Services;

/// <inheritdoc cref="IRoomService"/>
public sealed class RoomService : IRoomService
{
    private readonly IRoomRepository _roomRepository;
    private readonly ISlotRepository _slotRepository;
    private readonly TimeProvider _timeProvider;

    public RoomService(
        IRoomRepository roomRepository,
        ISlotRepository slotRepository,
        TimeProvider timeProvider)
    {
        _roomRepository = roomRepository;
        _slotRepository = slotRepository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RoomResponse>> ListRoomsAsync(CancellationToken cancellationToken)
    {
        var rooms = await _roomRepository.ListAsync(cancellationToken);

        return [.. rooms.Select(ToResponse)];
    }

    public async Task<OperationResult<RoomResponse>> GetRoomAsync(int roomId, CancellationToken cancellationToken)
    {
        var room = await _roomRepository.FindByIdAsync(roomId, cancellationToken);

        return room is null
            ? OperationResult<RoomResponse>.Failure(RoomErrorCodes.RoomNotFound)
            : OperationResult<RoomResponse>.Success(ToResponse(room));
    }

    public async Task<OperationResult<RoomResponse>> CreateRoomAsync(
        CreateRoomRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var room = new Room { Name = request.Name, Capacity = request.Capacity };

        // The same window every other room is on, so a room created today is aligned with one
        // created a week ago rather than ending a week later.
        foreach (var window in SlotGrid.Generate(Today(), SlotGrid.HorizonDays, AppTimeZone.Instance))
        {
            room.Slots.Add(new Slot { StartUtc = window.StartUtc, EndUtc = window.EndUtc });
        }

        await _roomRepository.AddAsync(room, cancellationToken);

        return OperationResult<RoomResponse>.Success(ToResponse(room));
    }

    public async Task<OperationResult<RoomResponse>> UpdateRoomAsync(
        int roomId,
        UpdateRoomRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var updated = await _roomRepository.UpdateAsync(roomId, request.Name, request.Capacity, cancellationToken);

        // Built from the values just written rather than read back: the update set exactly these
        // two columns, so a second round trip could only tell us what we already know.
        return updated
            ? OperationResult<RoomResponse>.Success(new RoomResponse(roomId, request.Name, request.Capacity))
            : OperationResult<RoomResponse>.Failure(RoomErrorCodes.RoomNotFound);
    }

    public async Task<OperationResult> DeleteRoomAsync(int roomId, CancellationToken cancellationToken)
    {
        var outcome = await _roomRepository.DeleteAsync(roomId, cancellationToken);

        return outcome switch
        {
            RoomDeleteOutcome.Deleted => OperationResult.Success(),
            RoomDeleteOutcome.HasBookedSlots => OperationResult.Failure(RoomErrorCodes.RoomHasBookedSlots),
            RoomDeleteOutcome.NotFound => OperationResult.Failure(RoomErrorCodes.RoomNotFound),

            // Unreachable through the enum's declared members. It exists because a C# switch
            // expression over an enum is not exhaustive - any int can be cast to one - and a
            // silent fall-through here would report a failed delete as a success.
            _ => throw new ArgumentOutOfRangeException(nameof(roomId), outcome, "Unhandled delete outcome.")
        };
    }

    public async Task<OperationResult<ScheduleResponse>> GetScheduleAsync(
        int roomId,
        ScheduleRangeRequest range,
        int callerUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        // Read first, so a schedule for a room that does not exist is a 404 rather than an empty
        // grid. It also supplies the room's name for the response.
        var room = await _roomRepository.FindByIdAsync(roomId, cancellationToken);

        if (room is null)
        {
            return OperationResult<ScheduleResponse>.Failure(RoomErrorCodes.RoomNotFound);
        }

        var (fromUtc, toUtc) = ResolveRange(range);

        var slots = await _slotRepository.ListForRoomAsync(roomId, fromUtc, toUtc, cancellationToken);

        var scheduleSlots = slots
            .Select(slot => new ScheduleSlotResponse(
                slot.Id,
                slot.StartUtc,
                slot.EndUtc,
                slot.BookedByUserId is not null,

                // int? == int is a lifted comparison: null is not equal to anything, so a free
                // slot is never reported as the caller's own.
                slot.BookedByUserId == callerUserId))
            .ToList();

        return OperationResult<ScheduleResponse>.Success(
            new ScheduleResponse(room.Id, room.Name, AppTimeZone.IanaId, scheduleSlots));
    }

    /// <summary>
    /// Absent bounds become the whole current window, so a client that wants "the schedule" does
    /// not have to work out where a local day begins in UTC.
    /// </summary>
    private (DateTime FromUtc, DateTime ToUtc) ResolveRange(ScheduleRangeRequest range)
    {
        var today = Today();

        return (
            range.FromUtc?.UtcDateTime ?? StartOfLocalDayUtc(today),
            range.ToUtc?.UtcDateTime ?? StartOfLocalDayUtc(today.AddDays(SlotGrid.HorizonDays)));
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, AppTimeZone.Instance));

    /// <summary>
    /// Midnight is safe to convert in this zone: its daylight-saving transitions happen at
    /// 03:00/04:00 local, so the start of a day is never a local time that does not exist.
    /// </summary>
    private static DateTime StartOfLocalDayUtc(DateOnly localDay) =>
        TimeZoneInfo.ConvertTimeToUtc(
            localDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            AppTimeZone.Instance);

    private static RoomResponse ToResponse(Room room) => new(room.Id, room.Name, room.Capacity);
}
