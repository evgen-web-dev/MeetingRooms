using System.Net;
using System.Text.RegularExpressions;
using MeetingRooms.Application.Errors;
using MeetingRooms.Domain;

namespace MeetingRooms.ConcurrencyTests;

/// <summary>
/// Three things earlier phases recorded as unproven, each of which needed a booking to exist or a
/// round trip through SQL Server, and so could not be pinned until now.
/// </summary>
[Collection(BookingCollection.Name)]
public sealed partial class ScheduleContractTests
{
    private readonly BookingScenario _scenario;

    public ScheduleContractTests(BookingScenario scenario)
    {
        _scenario = scenario;
    }

    /// <summary>
    /// Phase 4's regression, which until now had no test: <c>datetime2</c> carries no zone, so EF
    /// materialises these columns as <c>Unspecified</c> and <c>System.Text.Json</c> then writes
    /// them with no trailing <c>Z</c> - which a browser reads as local time and shifts by the
    /// viewer's offset. A value converter restores the kind.
    /// <para>
    /// Asserted against the <strong>raw JSON</strong> on purpose. Deserialising into a
    /// <c>DateTime</c> is exactly what hides the defect, because the two compare equal in C#.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ScheduleInstants_AreWrittenAsUtc()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _scenario.CreateClientFor(_scenario.Racers[0].Token);
        var rawJson = await client.GetStringAsync($"/api/rooms/{_scenario.RoomId}/schedule", cancellationToken);

        var instants = InstantProperties().Matches(rawJson)
            .Select(match => match.Groups["instant"].Value)
            .ToList();

        Assert.NotEmpty(instants);
        Assert.All(instants, instant => Assert.EndsWith("Z", instant, StringComparison.Ordinal));
    }

    /// <summary>
    /// Phase 4 could verify <c>isBooked</c> and <c>isBookedByMe</c> on their false branch only,
    /// because nothing in the application could book a slot. This is the true branch - and the
    /// separation that matters: a second caller sees the slot is taken without learning by whom.
    /// </summary>
    [Fact]
    public async Task BookedSlot_IsMineToTheBookerAndMerelyTakenToEveryoneElse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var room = await _scenario.CreateRoomAsync(cancellationToken);

        var (bookerEmail, bookerToken) = _scenario.Racers[0];
        var otherToken = _scenario.Racers[1].Token;

        var schedule = await _scenario.GetScheduleAsync(room.Id, bookerToken, cancellationToken);
        Assert.Equal(AppTimeZone.IanaId, schedule.TimeZoneId);

        var slotId = schedule.Slots[^1].Id;

        using (var booked = await _scenario.BookAsync(bookerToken, slotId, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        }

        var asBooker = await _scenario.GetScheduleAsync(room.Id, bookerToken, cancellationToken);
        var asOther = await _scenario.GetScheduleAsync(room.Id, otherToken, cancellationToken);

        var mine = asBooker.Slots.Single(slot => slot.Id == slotId);
        var theirs = asOther.Slots.Single(slot => slot.Id == slotId);

        Assert.True(mine.IsBooked);
        Assert.True(mine.IsBookedByMe);

        Assert.True(theirs.IsBooked);
        Assert.False(theirs.IsBookedByMe);

        // Nothing on the wire names the booker for anyone but an administrator.
        using var client = _scenario.CreateClientFor(otherToken);
        var rawJson = await client.GetStringAsync($"/api/rooms/{room.Id}/schedule", cancellationToken);
        Assert.DoesNotContain(bookerEmail, rawJson, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Phase 4's one open qualifier. Its conditional delete was proven at the repository level
    /// against real SQL Server, but nothing could book a slot, so the mapper row turning
    /// <c>RoomHasBookedSlots</c> into a 409 was the single link never exercised end to end.
    /// </summary>
    [Fact]
    public async Task RoomWithABookedSlot_CannotBeDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var room = await _scenario.CreateRoomAsync(cancellationToken);

        var schedule = await _scenario.GetScheduleAsync(room.Id, _scenario.Racers[2].Token, cancellationToken);

        using (var booked = await _scenario.BookAsync(
                   _scenario.Racers[2].Token, schedule.Slots[^1].Id, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        }

        using var admin = _scenario.CreateClientFor(_scenario.AdminToken);
        using var response = await admin.DeleteAsync($"/api/rooms/{room.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            RoomErrorCodes.RoomHasBookedSlots,
            await SlotLifecycleTests.ErrorCodesOfAsync(response, cancellationToken));
    }

    [GeneratedRegex("\"(?:startUtc|endUtc|bookedAtUtc)\":\"(?<instant>[^\"]+)\"")]
    private static partial Regex InstantProperties();
}
