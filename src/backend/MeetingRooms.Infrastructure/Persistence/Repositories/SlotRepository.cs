using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
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

    /// <remarks>
    /// The whole no-double-booking guarantee is the WHERE clause below.
    /// <para>
    /// <c>ExecuteUpdateAsync</c> is the one EF API that bypasses the change tracker: it compiles
    /// to a single <c>UPDATE ... WHERE</c> and sends it, loading nothing. That matters, because
    /// everything else EF does is load-mutate-save - a read followed by a write, which is exactly
    /// the check-then-act the assignment rules out. Here the business condition travels <em>in</em>
    /// the statement, so the engine settles the race under the row lock before any count comes
    /// back. The loser blocks on the winner's lock, re-reads the committed row when it clears,
    /// fails <c>BookedByUserId IS NULL</c>, and updates nothing.
    /// </para>
    /// <para>
    /// Correct under READ COMMITTED with or without RCSI, which Azure SQL enables by default. It
    /// would <em>not</em> hold under SNAPSHOT, which raises update-conflict 3960 instead; this
    /// path deliberately uses the connection's default isolation level and opens no transaction of
    /// its own.
    /// </para>
    /// </remarks>
    public async Task<(SlotClaimOutcome Outcome, DateTime? BookedAtUtc)> TryClaimAsync(
        int slotId,
        int userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // EndUtc rather than StartUtc: a slot with twenty minutes left is still twenty minutes of
        // a meeting room. A slot stops being bookable when it is over, not when it begins.
        var claimed = await _dbContext.Set<Slot>()
            .Where(slot => slot.Id == slotId
                        && slot.BookedByUserId == null
                        && slot.EndUtc > nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(slot => slot.BookedByUserId, userId)
                    .SetProperty(slot => slot.BookedAtUtc, nowUtc),
                cancellationToken);

        if (claimed == 1)
        {
            // nowUtc arrives truncated to whole seconds, so this is exactly what the datetime2(0)
            // column now holds - no read-back is needed to report it honestly.
            return (SlotClaimOutcome.Claimed, nowUtc);
        }

        // Failure path only, and it reads three columns rather than the row: this runs precisely
        // when the slot is contended, so it should ask for as little as possible.
        var slot = await _dbContext.Set<Slot>()
            .AsNoTracking()
            .Where(slot => slot.Id == slotId)
            .Select(slot => new { slot.BookedByUserId, slot.BookedAtUtc, slot.EndUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (slot is null)
        {
            return (SlotClaimOutcome.NotFound, null);
        }

        // Ordered, because a slot can be both booked and expired and only one answer is useful.
        // Ownership first: a retry that lands after its own commit finds its own id here, and
        // telling the caller who actually won that they lost is the one misreport this design
        // refuses to accept.
        if (slot.BookedByUserId == userId)
        {
            // Both columns are written by the statement above and by nothing else, so a booker
            // without a booking time is a broken invariant rather than a case to handle.
            return (
                SlotClaimOutcome.AlreadyClaimedByCaller,
                slot.BookedAtUtc ?? throw new InvalidOperationException(
                    $"Slot {slotId} has a booker but no booking time."));
        }

        if (slot.BookedByUserId is not null)
        {
            return (SlotClaimOutcome.AlreadyBooked, null);
        }

        if (slot.EndUtc <= nowUtc)
        {
            return (SlotClaimOutcome.HasEnded, null);
        }

        // Unreachable, and provably so rather than optimistically: a booking is never removed,
        // EndUtc is never updated, and nowUtc is fixed for this whole call. A row that is free and
        // unexpired now was free and unexpired when the UPDATE ran, so the UPDATE would have
        // claimed it. Throwing beats returning the least-wrong enum member - a 500 here means
        // "this state should not exist", which is what an exception is for.
        throw new InvalidOperationException(
            $"Slot {slotId} is unbooked and unexpired, yet the conditional update matched no row.");
    }

    public async Task<IReadOnlyList<Slot>> ListBookedForUserAsync(
        int userId,
        CancellationToken cancellationToken) =>
        await _dbContext.Set<Slot>()
            .AsNoTracking()
            .Include(slot => slot.Room)
            .Where(slot => slot.BookedByUserId == userId)
            // StartUtc alone is not a total order - one user can hold two rooms in the same hour -
            // so the list would be free to come back in a different sequence on each call.
            .OrderBy(slot => slot.StartUtc)
            .ThenBy(slot => slot.RoomId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Slot>> ListAllBookedAsync(CancellationToken cancellationToken) =>
        // Including the booker materialises the whole AppUser row to reach one email address. It
        // is never serialised and the list is small, but it is a wider read than the response
        // needs; the alternative is a projection type in Application that would be a wire contract
        // in all but name. This is the line to revisit if the admin list ever grows a page size.
        await _dbContext.Set<Slot>()
            .AsNoTracking()
            .Include(slot => slot.Room)
            .Include(slot => slot.BookedByUser)
            .Where(slot => slot.BookedByUserId != null)
            .OrderBy(slot => slot.StartUtc)
            .ThenBy(slot => slot.RoomId)
            .ToListAsync(cancellationToken);
}
