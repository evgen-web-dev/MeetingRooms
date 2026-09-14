using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using MeetingRooms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRoomRepository"/>
public sealed class RoomRepository : IRoomRepository
{
    private readonly AppDbContext _dbContext;

    public RoomRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken) =>
        await _dbContext.Set<Room>()
            .AsNoTracking()
            .OrderBy(room => room.Name)
            .ToListAsync(cancellationToken);

    public async Task<Room?> FindByIdAsync(int roomId, CancellationToken cancellationToken) =>
        await _dbContext.Set<Room>()
            .AsNoTracking()
            .FirstOrDefaultAsync(room => room.Id == roomId, cancellationToken);

    public async Task AddAsync(Room room, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(room);

        // One Add for the whole graph: EF inserts the room, reads back its identity value and
        // stamps it onto every slot in the collection, inside a single implicit transaction. A
        // room therefore cannot come into existence without its grid.
        _dbContext.Set<Room>().Add(room);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(int roomId, string name, int capacity, CancellationToken cancellationToken)
    {
        // One UPDATE, no load-modify-save round trip, and the rows-affected count doubles as the
        // existence check.
        var updated = await _dbContext.Set<Room>()
            .Where(room => room.Id == roomId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(room => room.Name, name)
                    .SetProperty(room => room.Capacity, capacity),
                cancellationToken);

        return updated == 1;
    }

    public async Task<RoomDeleteOutcome> DeleteAsync(int roomId, CancellationToken cancellationToken)
    {
        // The condition lives in the WHERE clause rather than in a check before the delete, so
        // there is no window in which a slot can be booked between deciding and deleting. This is
        // the same shape phase 5's booking claim uses, in a place where the stakes are lower.
        //
        // The slots go because the database cascades them: ExecuteDelete issues one DELETE and
        // never runs an EF-side cascade, so a relationship configured ClientCascade would fail
        // here on a foreign-key violation instead.
        var deleted = await _dbContext.Set<Room>()
            .Where(room => room.Id == roomId && !room.Slots.Any(slot => slot.BookedByUserId != null))
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 1)
        {
            return RoomDeleteOutcome.Deleted;
        }

        // Only on the failure path, and only to tell 404 from 409: zero rows means either that
        // no such room exists or that the one that does has a booking on it.
        return await _dbContext.Set<Room>().AnyAsync(room => room.Id == roomId, cancellationToken)
            ? RoomDeleteOutcome.HasBookedSlots
            : RoomDeleteOutcome.NotFound;
    }
}
