namespace MeetingRooms.Api.Hubs;

/// <summary>
/// What the server may call on a connected schedule viewer.
/// <para>
/// A .NET idiom with no JavaScript counterpart: declaring this interface and deriving the hub from
/// <c>Hub&lt;IScheduleClient&gt;</c> makes SignalR generate the client proxy, so the server half of
/// the contract is checked by the compiler instead of being a string literal. The browser half
/// stays a string - <c>connection.on('SlotBooked', …)</c> - which is as much as the platform can
/// give, and it is the half that a rename would otherwise break silently.
/// </para>
/// </summary>
public interface IScheduleClient
{
    /// <summary>One of this room's slots has just been claimed.</summary>
    Task SlotBooked(SlotBookedEvent slotBooked);
}
