using Microsoft.AspNetCore.SignalR;

namespace MeetingRooms.Api.Hubs;

/// <summary>
/// Empty on purpose. Phase 1 needs a hub endpoint only so the deployed app can prove that
/// Azure SignalR negotiate works before anything depends on it; per-room groups, methods
/// and broadcasts arrive in phase 6.
/// </summary>
public sealed class ScheduleHub : Hub
{
}
