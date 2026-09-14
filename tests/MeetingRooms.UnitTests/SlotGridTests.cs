using MeetingRooms.Domain;

namespace MeetingRooms.UnitTests;

/// <summary>
/// The generator is pure, so every daylight-saving case is reachable from a unit test rather
/// than only in late October. The dates below were taken from the zone data rather than assumed:
/// Ukraine still observes EU daylight saving, switching on 2026-03-29 and 2026-10-25, both at
/// 03:00/04:00 local and therefore outside the working day.
/// </summary>
public sealed class SlotGridTests
{
    private static readonly TimeZoneInfo Zone = AppTimeZone.Instance;

    private static readonly DateOnly SummerDay = new(2026, 9, 15);      // EEST, UTC+3
    private static readonly DateOnly WinterDay = new(2026, 11, 15);     // EET,  UTC+2

    [Fact]
    public void TheWorkingDayIsTenSlotsLong() =>
        Assert.Equal(10, SlotGrid.SlotsPerDay);

    [Fact]
    public void GeneratesEveryDayInTheRequestedRange()
    {
        var windows = SlotGrid.Generate(SummerDay, SlotGrid.HorizonDays, Zone);

        Assert.Equal(SlotGrid.HorizonDays * SlotGrid.SlotsPerDay, windows.Count);
        Assert.Equal(140, windows.Count);
    }

    [Fact]
    public void CoversConsecutiveLocalDays()
    {
        var windows = SlotGrid.Generate(SummerDay, SlotGrid.HorizonDays, Zone);

        for (var dayOffset = 0; dayOffset < SlotGrid.HorizonDays; dayOffset++)
        {
            var firstOfDay = windows[dayOffset * SlotGrid.SlotsPerDay];

            Assert.Equal(SummerDay.AddDays(dayOffset), LocalDateOf(firstOfDay.StartUtc));
        }
    }

    [Fact]
    public void EveryWindowIsExactlyOneSlotLong()
    {
        var windows = SlotGrid.Generate(SummerDay, SlotGrid.HorizonDays, Zone);

        Assert.All(windows, window => Assert.Equal(SlotGrid.SlotLength, window.EndUtc - window.StartUtc));
    }

    [Fact]
    public void EveryInstantIsUtc()
    {
        var windows = SlotGrid.Generate(SummerDay, SlotGrid.HorizonDays, Zone);

        Assert.All(windows, window =>
        {
            Assert.Equal(DateTimeKind.Utc, window.StartUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, window.EndUtc.Kind);
        });
    }

    [Fact]
    public void WindowsWithinADayAreContiguous()
    {
        var windows = SlotGrid.Generate(SummerDay, 1, Zone);

        for (var index = 1; index < windows.Count; index++)
        {
            Assert.Equal(windows[index - 1].EndUtc, windows[index].StartUtc);
        }
    }

    [Fact]
    public void WindowsNeverOverlapAcrossTheWholeRange()
    {
        var windows = SlotGrid.Generate(SummerDay, SlotGrid.HorizonDays, Zone);

        for (var index = 1; index < windows.Count; index++)
        {
            Assert.True(windows[index].StartUtc >= windows[index - 1].EndUtc);
        }
    }

    [Theory]
    [InlineData(2026, 9, 15, 5)]    // summer time, UTC+3
    [InlineData(2026, 11, 15, 6)]   // winter time, UTC+2
    [InlineData(2026, 3, 29, 5)]    // spring-forward day: already on summer time by 08:00
    [InlineData(2026, 10, 25, 6)]   // autumn-back day: already on winter time by 08:00
    public void TheDayRunsFromDayStartToDayEndLocallyWhateverTheOffset(
        int year, int month, int day, int expectedFirstStartUtcHour)
    {
        var windows = SlotGrid.Generate(new DateOnly(year, month, day), 1, Zone);

        Assert.Equal(expectedFirstStartUtcHour, windows[0].StartUtc.Hour);
        Assert.Equal(SlotGrid.DayStart, LocalTimeOf(windows[0].StartUtc));
        Assert.Equal(SlotGrid.DayEnd, LocalTimeOf(windows[^1].EndUtc));
    }

    [Theory]
    [InlineData(2026, 3, 29)]
    [InlineData(2026, 10, 25)]
    public void ADaylightSavingTransitionDayIsStillAnOrdinaryWorkingDay(int year, int month, int day)
    {
        var windows = SlotGrid.Generate(new DateOnly(year, month, day), 1, Zone);

        Assert.Equal(SlotGrid.SlotsPerDay, windows.Count);
        Assert.All(windows, window => Assert.Equal(SlotGrid.SlotLength, window.EndUtc - window.StartUtc));
    }

    /// <summary>
    /// The property the whole design rests on, stated directly: crossing a transition moves the
    /// UTC instant by an hour and leaves the local hour where it was.
    /// </summary>
    [Fact]
    public void CrossingATransitionShiftsTheInstantButNotTheLocalHour()
    {
        var windows = SlotGrid.Generate(new DateOnly(2026, 10, 24), 3, Zone);

        var dayStarts = windows
            .Where((_, index) => index % SlotGrid.SlotsPerDay == 0)
            .ToList();

        Assert.Equal(new[] { 5, 6, 6 }, dayStarts.Select(window => window.StartUtc.Hour));
        Assert.All(dayStarts, window => Assert.Equal(SlotGrid.DayStart, LocalTimeOf(window.StartUtc)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefusesANonPositiveDayCount(int days) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SlotGrid.Generate(SummerDay, days, Zone));

    [Fact]
    public void RefusesANullZone() =>
        Assert.Throws<ArgumentNullException>(() => SlotGrid.Generate(WinterDay, 1, null!));

    private static TimeOnly LocalTimeOf(DateTime utc) =>
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));

    private static DateOnly LocalDateOf(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
}
