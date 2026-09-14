using MeetingRooms.Domain;
using MeetingRooms.Domain.Entities;
using MeetingRooms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetingRooms.Infrastructure.Seeders;

/// <summary>
/// Brings every room's grid up to the current window, and is why the horizon is rolling rather
/// than fixed at whenever the database was first seeded.
/// <para>
/// Without it the grid would be a window frozen at first seeding: rooms created later would end
/// earlier than their neighbours, and the whole application would show an empty schedule once
/// fourteen days had passed - recoverable only by widening a constant and redeploying.
/// </para>
/// <para>
/// Idempotent by construction, and a no-op on a second start of the same day. The unique index on
/// <c>(RoomId, StartUtc)</c> is the backstop if that reasoning is ever wrong.
/// </para>
/// </summary>
public static class SlotGridTopUp
{
    public static async Task RunAsync(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        var dbContext = scopedServices.GetRequiredService<AppDbContext>();
        var timeProvider = scopedServices.GetRequiredService<TimeProvider>();
        var logger = scopedServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SlotGridTopUp));

        var roomIds = await dbContext.Set<Room>().Select(room => room.Id).ToListAsync();

        if (roomIds.Count == 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, AppTimeZone.Instance));

        var windows = SlotGrid.Generate(today, SlotGrid.HorizonDays, AppTimeZone.Instance);

        // Hoisted rather than indexed inside the query: an expression tree may not contain a
        // from-end index, so windows[^1] does not compile in a Where clause. (C# lets you write
        // it anywhere else, which is what makes the error surprising the first time.)
        var firstStartUtc = windows[0].StartUtc;
        var lastStartUtc = windows[^1].StartUtc;

        // One round trip for what already exists, rather than an existence check per window. The
        // bounds are the first and last generated starts, so this is a range seek on the
        // (RoomId, StartUtc) index.
        var existingStarts = await dbContext.Set<Slot>()
            .Where(slot => slot.StartUtc >= firstStartUtc && slot.StartUtc <= lastStartUtc)
            .Select(slot => new { slot.RoomId, slot.StartUtc })
            .ToListAsync();

        var alreadyThere = existingStarts
            .Select(slot => (slot.RoomId, slot.StartUtc))
            .ToHashSet();

        var missing = (
            from roomId in roomIds
            from window in windows
            where !alreadyThere.Contains((roomId, window.StartUtc))
            select new Slot
            {
                RoomId = roomId,
                StartUtc = window.StartUtc,
                EndUtc = window.EndUtc
            }).ToList();

        if (missing.Count == 0)
        {
            logger.LogInformation("Slot grid is already current for {RoomCount} room(s); inserted none.", roomIds.Count);

            return;
        }

        dbContext.Set<Slot>().AddRange(missing);

        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Topped up the slot grid with {SlotCount} slot(s) across {RoomCount} room(s), through {Through}.",
            missing.Count,
            roomIds.Count,
            today.AddDays(SlotGrid.HorizonDays - 1));
    }
}
