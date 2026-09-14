using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingRooms.Application.DTOs.Bookings;
using MeetingRooms.Application.Errors;
using MeetingRooms.Domain.Entities;
using MeetingRooms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRooms.ConcurrencyTests;

/// <summary>
/// Assignment requirement #6: fire many simultaneous booking requests at one slot and assert that
/// exactly one booking is created.
/// <para>
/// This test is only meaningful if it can be made to fail. Delete <c>&amp;&amp;
/// slot.BookedByUserId == null</c> from <c>SlotRepository.TryClaimAsync</c> and it must go red -
/// that mutation probe, not a green run, is what <c>docs/plan.md</c> names as the phase's exit
/// criterion.
/// </para>
/// </summary>
[Collection(BookingCollection.Name)]
public sealed class SlotBookingConcurrencyTests
{
    private readonly BookingScenario _scenario;

    public SlotBookingConcurrencyTests(BookingScenario scenario)
    {
        _scenario = scenario;
    }

    [Fact]
    public async Task OneSlot_TwentySimultaneousRequests_ProducesExactlyOneBooking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The far end of the horizon, roughly a fortnight out: no clock boundary can reach it, so
        // the test cannot become flaky by being run late in the day.
        var schedule = await _scenario.GetScheduleAsync(_scenario.Racers[0].Token, cancellationToken);
        var slot = schedule.Slots[^1];

        Assert.False(slot.IsBooked, "the slot under test must start free");

        // One gate, twenty waiters. RunContinuationsAsynchronously keeps the thread that opens the
        // gate from running each waiter's prologue itself, in turn. It is not on its own what makes
        // the requests overlap - measured, not assumed: with it removed they still overlap, because
        // each continuation only runs inline until its HTTP call suspends. The assertion after the
        // race is what actually establishes overlap.
        // The pool ramps new worker threads in slowly by default, which would stagger the start
        // of twenty near-simultaneous continuations for reasons that have nothing to do with the
        // code under test.
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.SetMinThreads(Math.Max(workerThreads, BookingScenario.RacingUsers * 2), completionPortThreads);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = _scenario.Racers
            .Select(async (racer, index) =>
            {
                // Its own client, so no connection or handler is shared and nothing serialises
                // the requests behind a single one.
                using var client = _scenario.CreateClientFor(racer.Token);

                await gate.Task;

                var startedAt = Stopwatch.GetTimestamp();
                var response = await client.PostAsJsonAsync(
                    "/api/bookings", new BookSlotRequest(slot.Id), cancellationToken);
                var finishedAt = Stopwatch.GetTimestamp();

                return (
                    Index: index,
                    response.StatusCode,
                    Body: await response.Content.ReadAsStringAsync(cancellationToken),
                    StartedAt: startedAt,
                    FinishedAt: finishedAt);
            })
            .ToList();

        gate.SetResult();

        var attemptResults = await Task.WhenAll(attempts);

        var winners = attemptResults.Where(attempt => attempt.StatusCode == HttpStatusCode.Created).ToList();
        var losers = attemptResults.Where(attempt => attempt.StatusCode == HttpStatusCode.Conflict).ToList();

        // Genuine overlap, not merely twenty sequential requests: the last request to be issued
        // went out before the first one came back, so at one instant all twenty were in flight.
        // Without this the suite would pass identically if they had run one after another, and
        // the mutation probe cannot tell the difference either - with the predicate removed every
        // UPDATE matches whatever the ordering. This is docs/plan.md risk 2, and it is the only
        // assertion here that actually retires it.
        Assert.True(
            attemptResults.Max(attempt => attempt.StartedAt) < attemptResults.Min(attempt => attempt.FinishedAt),
            "the requests did not overlap: every one finished before the last one started");

        // The assignment's three clauses, in its own order: exactly one succeeds, the rest get a
        // clear conflict, and nobody gets a server error.
        Assert.Single(winners);
        Assert.Equal(BookingScenario.RacingUsers - 1, losers.Count);
        Assert.DoesNotContain(attemptResults, attempt => (int)attempt.StatusCode >= 500);

        Assert.All(losers, loser =>
            Assert.Contains(BookingErrorCodes.SlotAlreadyBooked, ErrorCodesOf(loser.Body)));

        // And the claim the HTTP responses cannot make on their own: the database holds one
        // booking, by the caller who was told they had won.
        using var scope = _scenario.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var bookedRow = await dbContext.Set<Slot>()
            .AsNoTracking()
            .Where(bookedSlot => bookedSlot.Id == slot.Id)
            .Select(bookedSlot => new { bookedSlot.BookedByUserId, bookedSlot.BookedAtUtc })
            .SingleAsync(cancellationToken);

        var bookerEmail = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == bookedRow.BookedByUserId)
            .Select(user => user.Email)
            .SingleAsync(cancellationToken);

        Assert.Equal(_scenario.Racers[winners[0].Index].Email, bookerEmail);

        // The winner was told the truth about when, too - no rounding drift between the value
        // reported and the value stored.
        var reported = JsonSerializer.Deserialize<BookSlotResponse>(
            winners[0].Body, JsonSerializerOptions.Web);

        Assert.Equal(bookedRow.BookedAtUtc, reported?.BookedAtUtc);
    }

    private static IReadOnlyList<string> ErrorCodesOf(string problemDetailsBody) =>
        [.. JsonDocument.Parse(problemDetailsBody).RootElement
            .GetProperty("errorDetails")
            .EnumerateArray()
            .Select(code => code.GetString() ?? string.Empty)];
}
