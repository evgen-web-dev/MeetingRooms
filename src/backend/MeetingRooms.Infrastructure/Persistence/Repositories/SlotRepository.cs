using MeetingRooms.Application.Interfaces;
using MeetingRooms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ISlotRepository"/>
public sealed class SlotRepository : ISlotRepository
{
    private readonly AppDbContext _dbContext;

    public SlotRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Slot>> ListForRoomAsync(
        int roomId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken) =>
        // One seek on the unique (RoomId, StartUtc) index and no join: the caller's own
        // relationship to a slot is decided from BookedByUserId, which is already on the row.
        await _dbContext.Set<Slot>()
            .AsNoTracking()
            .Where(slot => slot.RoomId == roomId && slot.StartUtc >= fromUtc && slot.StartUtc < toUtc)
            .OrderBy(slot => slot.StartUtc)
            .ToListAsync(cancellationToken);
}
