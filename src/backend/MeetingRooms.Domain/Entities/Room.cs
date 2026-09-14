namespace MeetingRooms.Domain.Entities;

/// <summary>
/// A bookable meeting room. A name and a capacity are everything a room carries -
/// <c>docs/requirements.md</c> §2 - so there is no description, no location and no owner.
/// </summary>
public sealed class Room
{
    /// <summary>
    /// Long enough for any name a person would type, short enough to index. Declared here rather
    /// than in the EF configuration because the request validator needs the same number, and two
    /// copies is how a column and the rule guarding it drift apart.
    /// </summary>
    public const int NameMaxLength = 100;

    public int Id { get; set; }

    public required string Name { get; set; }

    public int Capacity { get; set; }

    /// <summary>
    /// This room's slots. Load-bearing rather than decoration: creating a room inserts the room
    /// and its entire grid through this collection in one <c>SaveChangesAsync</c>, with EF
    /// filling each slot's <see cref="Slot.RoomId"/> from the identity value the room's own
    /// insert generates. Without the navigation that would be a second round trip, and a
    /// partially created room if it failed.
    /// </summary>
    public ICollection<Slot> Slots { get; } = [];
}
