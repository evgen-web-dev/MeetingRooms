using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingRooms.Application.DTOs.Bookings;
using MeetingRooms.Application.Errors;

namespace MeetingRooms.ConcurrencyTests;

/// <summary>
/// The rule that a slot stops being bookable when it <em>ends</em>, not when it starts.
/// <para>
/// Both cases depend on where the clock is relative to a slot's window, and the generated grid
/// only contains an expired slot if the suite happens to run late in the Kyiv working day. Rather
/// than skip conditionally or substitute a clock, each test inserts the slot it needs - which is
/// deterministic at any hour and gives the <c>EndUtc</c> predicate a mutation probe of its own.
/// </para>
/// </summary>
[Collection(BookingCollection.Name)]
public sealed class SlotLifecycleTests
{
    private readonly BookingScenario _scenario;

    public SlotLifecycleTests(BookingScenario scenario)
    {
        _scenario = scenario;
    }

    [Fact]
    public async Task SlotWhoseWindowHasClosed_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var room = await _scenario.CreateRoomAsync(cancellationToken);

        var endedAt = WholeSecondsAgo(TimeSpan.FromHours(1));
        var slotId = await _scenario.InsertFreeSlotAsync(
            room.Id, endedAt.AddHours(-1), endedAt, cancellationToken);

        using var response = await _scenario.BookAsync(_scenario.Racers[0].Token, slotId, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            BookingErrorCodes.SlotHasEnded,
            await ErrorCodesOfAsync(response, cancellationToken));
    }

    /// <summary>
    /// The deliberate other half of the rule: a slot with half an hour left is still half an hour
    /// of a meeting room, so it books.
    /// </summary>
    [Fact]
    public async Task SlotAlreadyUnderWayButNotFinished_IsBookable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var room = await _scenario.CreateRoomAsync(cancellationToken);

        var startedAt = WholeSecondsAgo(TimeSpan.FromMinutes(30));
        var slotId = await _scenario.InsertFreeSlotAsync(
            room.Id, startedAt, startedAt.AddHours(1), cancellationToken);

        using var response = await _scenario.BookAsync(_scenario.Racers[1].Token, slotId, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var booking = await response.Content.ReadFromJsonAsync<BookSlotResponse>(cancellationToken);
        Assert.Equal(slotId, booking?.SlotId);
    }

    /// <summary>
    /// Seconds deliberately non-zero: every generated slot starts exactly on the hour, so this can
    /// never collide with the unique (RoomId, StartUtc) index.
    /// </summary>
    private static DateTime WholeSecondsAgo(TimeSpan howLong)
    {
        var instant = DateTime.UtcNow.Subtract(howLong);

        return new DateTime(instant.Ticks - (instant.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc)
            .AddSeconds(instant.Second == 0 ? 13 : 0);
    }

    internal static async Task<IReadOnlyList<string>> ErrorCodesOfAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return [.. document.RootElement.GetProperty("errorDetails")
            .EnumerateArray()
            .Select(code => code.GetString() ?? string.Empty)];
    }
}
