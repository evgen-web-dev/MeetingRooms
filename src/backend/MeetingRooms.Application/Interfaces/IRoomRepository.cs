using MeetingRooms.Application.Results;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Reads and writes rooms. Methods are named for the use case rather than for CRUD, and each owns
/// its own save, so there is no <c>SaveChanges</c> a caller can forget.
/// <para>
/// <see cref="IUnitOfWork"/> is deliberately absent here. Every operation below is a single
/// statement or a single <c>SaveChanges</c>, which EF already wraps in an implicit transaction;
/// the unit of work exists for the one use case that writes twice (<c>docs/decisions.md</c>).
/// </para>
/// </summary>
public interface IRoomRepository
{
    Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken);

    Task<Room?> FindByIdAsync(int roomId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the room together with whatever its <see cref="Room.Slots"/> collection holds, in
    /// one <c>SaveChanges</c> - so a room cannot come into existence without its grid. The
    /// generated key is on <paramref name="room"/> when this returns.
    /// </summary>
    Task AddAsync(Room room, CancellationToken cancellationToken);

    /// <summary>Returns <c>false</c> when no room has that id.</summary>
    Task<bool> UpdateAsync(int roomId, string name, int capacity, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the room and, through the database's cascade, its slots - but only while none of
    /// them is booked. The condition is part of the delete rather than a check before it, so
    /// there is no window in which a slot can be booked between the two.
    /// </summary>
    Task<RoomDeleteOutcome> DeleteAsync(int roomId, CancellationToken cancellationToken);
}
