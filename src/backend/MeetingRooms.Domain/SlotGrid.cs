namespace MeetingRooms.Domain;

/// <summary>
/// Generates the bookable grid: the same working day, every day, for every room
/// (<c>docs/requirements.md</c> §3).
/// <para>
/// Pure and static by design - it takes a day, a count and a zone, and returns windows. Nothing
/// here reads the clock or touches a database, which is what makes the daylight-saving
/// behaviour testable without waiting for October.
/// </para>
/// </summary>
public static class SlotGrid
{
    /// <summary>First slot of the day starts here, in <see cref="AppTimeZone"/>'s zone.</summary>
    public static readonly TimeOnly DayStart = new(8, 0);

    /// <summary>Last slot of the day ends here. Exclusive, so no slot begins at this time.</summary>
    public static readonly TimeOnly DayEnd = new(18, 0);

    public static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(60);

    /// <summary>How far ahead the grid is kept populated, in days.</summary>
    public const int HorizonDays = 14;

    /// <summary>
    /// Derived rather than declared, so it cannot disagree with the three constants above if one
    /// of them is ever changed.
    /// </summary>
    public static int SlotsPerDay { get; } = (int)((DayEnd - DayStart) / SlotLength);

    /// <summary>
    /// Every window for <paramref name="days"/> consecutive days starting at
    /// <paramref name="firstDay"/>, ordered.
    /// </summary>
    /// <param name="firstDay">The first local calendar day to generate, in <paramref name="zone"/>.</param>
    /// <param name="days">How many consecutive days to generate. Must be positive.</param>
    /// <param name="zone">The zone the working day is measured in.</param>
    public static IReadOnlyList<SlotWindow> Generate(DateOnly firstDay, int days, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);

        var windows = new List<SlotWindow>(days * SlotsPerDay);

        for (var dayOffset = 0; dayOffset < days; dayOffset++)
        {
            var day = firstDay.AddDays(dayOffset);

            for (var slotIndex = 0; slotIndex < SlotsPerDay; slotIndex++)
            {
                // DateTimeKind.Unspecified is required, not stylistic: ConvertTimeToUtc throws
                // when the value's Kind is Utc or Local and contradicts the zone argument.
                // Unspecified is the only kind that means "a wall-clock reading, interpret it in
                // this zone" - there is no type here that carries its own zone.
                var localStart = day.ToDateTime(
                    DayStart.Add(SlotLength * slotIndex),
                    DateTimeKind.Unspecified);

                // Safe across a daylight-saving boundary because the working day never contains
                // one: the EU switches at 03:00/04:00 local, outside 08:00-18:00. So no local
                // time generated here can be one that does not exist (spring forward) or one
                // that happens twice (autumn back) - the two cases ConvertTimeToUtc would
                // otherwise resolve by rule rather than by intent.
                windows.Add(new SlotWindow(
                    TimeZoneInfo.ConvertTimeToUtc(localStart, zone),
                    TimeZoneInfo.ConvertTimeToUtc(localStart.Add(SlotLength), zone)));
            }
        }

        return windows;
    }
}
