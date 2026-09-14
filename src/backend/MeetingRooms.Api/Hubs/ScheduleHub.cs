using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRooms.Api.Hubs;

/// <summary>
/// Live schedule updates, one group per room.
/// <para>
/// Clients subscribe to the room they are looking at, so viewers of room A are not woken by
/// activity in room B (<c>docs/requirements.md</c> §6). The hub only manages membership; the
/// broadcast itself comes from <see cref="Realtime.SignalRScheduleNotifier"/>, after the booking
/// write has committed.
/// </para>
/// <para>
/// <strong>No database access here, deliberately.</strong> Subscribing to a room that does not
/// exist is harmless - nothing is ever published to that group - and any authenticated caller may
/// read any room's schedule, so membership needs no check beyond <see cref="AuthorizeAttribute"/>.
/// An existence check would put a repository and a scoped <c>DbContext</c> behind a long-lived
/// connection to prevent nothing.
/// </para>
/// </summary>
[Authorize]
public sealed class ScheduleHub : Hub<IScheduleClient>
{
    /// <summary>
    /// The group name for one room's viewers. Composed here and used by the notifier, so the name
    /// a connection joins and the name an event is sent to cannot drift apart - a group name has
    /// no registry behind it, and a typo in one of two places is a broadcast nobody receives and
    /// no error anywhere.
    /// </summary>
    /// <remarks>
    /// The id is formatted invariantly rather than interpolated directly: string interpolation
    /// formats numbers in the current culture, and a group name has to match as an exact string.
    /// </remarks>
    public static string RoomGroup(int roomId) => $"room-{roomId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Starts delivering this room's updates to the calling connection.</summary>
    public Task SubscribeToRoom(int roomId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));

    /// <summary>
    /// Stops delivering them. Membership is per <em>connection</em> and dies with it, so this is
    /// only needed when a client switches rooms without reconnecting.
    /// </summary>
    public Task UnsubscribeFromRoom(int roomId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(roomId));
}
