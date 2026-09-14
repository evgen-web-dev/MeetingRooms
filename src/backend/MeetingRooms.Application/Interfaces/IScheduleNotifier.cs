namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Announces a change to a room's schedule to whoever is currently viewing it.
/// <para>
/// The port exists so that this layer can say <em>what happened</em> without naming a transport.
/// SignalR is an implementation detail of <c>Api</c>, which owns the hub, the group names and the
/// shape that goes on the wire; nothing below it references SignalR at all.
/// </para>
/// </summary>
public interface IScheduleNotifier
{
    /// <summary>
    /// Announces that a slot has been claimed, to viewers of the room it belongs to.
    /// <para>
    /// Called only for a real state change. A caller re-posting a booking they already hold is a
    /// successful request in which nothing changed, and announcing it would tell every viewer that
    /// something happened when nothing did - see <c>SlotClaimOutcome.AlreadyClaimedByCaller</c>.
    /// </para>
    /// <para>
    /// <strong>Implementations must not throw.</strong> This is called after the claim has
    /// committed, on a booking that cannot be undone: a failed announcement leaves other viewers
    /// stale until they read the schedule again, which is recoverable, whereas failing the request
    /// would tell the one caller who did get the room that they did not. Transport failures belong
    /// in the log, and in the adapter that owns the transport.
    /// </para>
    /// </summary>
    /// <param name="roomId">The room whose viewers are listening. Read from the slot after the claim.</param>
    /// <param name="slotId">The slot that was claimed.</param>
    /// <remarks>
    /// No <see cref="CancellationToken"/>, deliberately, against the grain of every other
    /// asynchronous method here. The token available at the call site is the booking request's, and
    /// that one is cancelled when the caller's connection drops - so forwarding it would mean a
    /// booker closing their tab at the wrong moment silently withholds the announcement from
    /// everybody else, for a write that has already happened.
    /// </remarks>
    Task SlotBookedAsync(int roomId, int slotId);
}
