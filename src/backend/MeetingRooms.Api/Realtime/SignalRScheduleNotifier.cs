using MeetingRooms.Api.Hubs;
using MeetingRooms.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRooms.Api.Realtime;

/// <inheritdoc cref="IScheduleNotifier"/>
/// <remarks>
/// The adapter half of the port: the only place in the application that knows announcements travel
/// over SignalR. <see cref="IHubContext{THub, TClient}"/> is how code outside a hub sends to one -
/// a hub instance itself lives for the length of a single invocation and is not something to
/// inject.
/// </remarks>
public sealed class SignalRScheduleNotifier : IScheduleNotifier
{
    private readonly IHubContext<ScheduleHub, IScheduleClient> _hubContext;
    private readonly ILogger<SignalRScheduleNotifier> _logger;

    public SignalRScheduleNotifier(
        IHubContext<ScheduleHub, IScheduleClient> hubContext,
        ILogger<SignalRScheduleNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SlotBookedAsync(int roomId, int slotId)
    {
        try
        {
            await _hubContext.Clients
                .Group(ScheduleHub.RoomGroup(roomId))
                .SlotBooked(new SlotBookedEvent(roomId, slotId));
        }
        catch (Exception exception)
        {
            // Catching Exception is normally a smell; here it is the port's stated contract, and
            // this is the layer that owns the transport. By the time this runs the claim has
            // committed and cannot be undone: a lost announcement leaves other viewers stale until
            // they read the schedule again, while letting this escape would answer the one caller
            // who did get the room with a 500 describing a booking that exists and is theirs.
            _logger.LogError(
                exception,
                "Failed to announce that slot {SlotId} in room {RoomId} was booked. The booking is committed; "
                + "viewers of this room stay stale until they reload the schedule.",
                slotId,
                roomId);
        }
    }
}
