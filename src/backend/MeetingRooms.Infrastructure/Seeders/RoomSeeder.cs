using MeetingRooms.Domain.Entities;
using MeetingRooms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetingRooms.Infrastructure.Seeders;

/// <summary>
/// Puts demo rooms in front of a reviewer, so the deployed application is populated rather than
/// an empty page with a "create your first room" prompt behind an admin login.
/// <para>
/// Runs in every environment, per <c>docs/decisions.md</c>. It touches <see cref="AppDbContext"/>
/// directly, which the same document allows seeders to do - growing a repository method with
/// exactly one caller would be machinery.
/// </para>
/// </summary>
public static class RoomSeeder
{
    /// <summary>
    /// A fixture rather than filler: the capacities span a deliberate range, so capacity is
    /// visibly a real field on a screen rather than the same number four times.
    /// </summary>
    private static readonly (string Name, int Capacity)[] DemoRooms =
    [
        ("Focus Room", 2),
        ("Huddle Space", 6),
        ("Board Room", 12),
        ("Training Room", 20)
    ];

    /// <summary>
    /// Creates the demo rooms when there are none. Rooms only - their slots come from
    /// <see cref="SlotGridTopUp"/>, which runs next, so slot creation stays on one code path
    /// whether a room was seeded or created through the API.
    /// </summary>
    public static async Task SeedDemoRoomsAsync(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        var dbContext = scopedServices.GetRequiredService<AppDbContext>();
        var logger = scopedServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RoomSeeder));

        // Any room at all means someone - a seeder or an administrator - has been here. Seeding
        // on top of that would resurrect rooms an admin deleted on purpose.
        if (await dbContext.Set<Room>().AnyAsync())
        {
            return;
        }

        dbContext.Set<Room>().AddRange(
            DemoRooms.Select(demoRoom => new Room { Name = demoRoom.Name, Capacity = demoRoom.Capacity }));

        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} demo rooms.", DemoRooms.Length);
    }
}
