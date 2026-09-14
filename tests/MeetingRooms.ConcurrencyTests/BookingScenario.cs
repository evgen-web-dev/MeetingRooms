using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Application.DTOs.Auth;
using MeetingRooms.Application.DTOs.Bookings;
using MeetingRooms.Application.DTOs.Rooms;
using MeetingRooms.Domain.Entities;
using MeetingRooms.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRooms.ConcurrencyTests;

/// <summary>
/// One application, one private room, and a set of logged-in users - built once and shared by
/// every test in the collection.
/// <para>
/// The room is created through the API rather than seeded, so each run books slots nobody else
/// will touch. That matters because bookings cannot be cancelled: a test that reused a shared
/// room would consume its grid one run at a time.
/// </para>
/// </summary>
public sealed class BookingScenario : IAsyncLifetime
{
    /// <summary>
    /// How many callers race for one slot. Also why they are distinct users: a caller who already
    /// holds a slot is answered 201, so twenty requests from one account would report twenty
    /// winners and prove nothing.
    /// </summary>
    public const int RacingUsers = 20;

    private const string RacerPassword = "Racer-Pass-1!";

    public BookingApiFactory Factory { get; } = new();

    public string AdminToken { get; private set; } = string.Empty;

    /// <summary>The racers, in a fixed order, so a winning index identifies an email.</summary>
    public IReadOnlyList<(string Email, string Token)> Racers { get; private set; } = [];

    public int RoomId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        using var client = Factory.CreateClient();

        AdminToken = await LogInAsync(client, BookingApiFactory.AdminEmail, BookingApiFactory.AdminPassword);

        var room = await CreateRoomAsync(client, AdminToken);
        RoomId = room.Id;

        var racers = new List<(string, string)>(RacingUsers);

        // A fresh set per run: emails are unique for the lifetime of the database, and this
        // catalog outlives any single run.
        var runId = Guid.NewGuid().ToString("N")[..8];

        for (var index = 0; index < RacingUsers; index++)
        {
            var email = $"racer-{runId}-{index}@meetingrooms.test";

            await RegisterAsync(client, email, RacerPassword);
            racers.Add((email, await LogInAsync(client, email, RacerPassword)));
        }

        Racers = racers;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>A client already carrying one racer's bearer token.</summary>
    public HttpClient CreateClientFor(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>
    /// The shared race room's grid. Callers pick from the far end of it, where no clock boundary
    /// can make a test flaky.
    /// </summary>
    public Task<ScheduleResponse> GetScheduleAsync(string token, CancellationToken cancellationToken) =>
        GetScheduleAsync(RoomId, token, cancellationToken);

    public async Task<ScheduleResponse> GetScheduleAsync(int roomId, string token, CancellationToken cancellationToken)
    {
        using var client = CreateClientFor(token);

        return await client.GetFromJsonAsync<ScheduleResponse>(
                   $"/api/rooms/{roomId}/schedule", cancellationToken)
               ?? throw new InvalidOperationException("The schedule response was empty.");
    }

    /// <summary>
    /// A room of this test's own, with its own grid. Tests that book use one rather than the
    /// shared race room: a booking cannot be undone, so shared state here would mean tests
    /// consuming each other's slots.
    /// </summary>
    public async Task<RoomResponse> CreateRoomAsync(CancellationToken cancellationToken)
    {
        using var client = Factory.CreateClient();

        return await CreateRoomAsync(client, AdminToken);
    }

    /// <summary>
    /// Inserts one slot straight through the context, which is how the window-dependent cases stay
    /// deterministic at any hour of the day without a substituted clock.
    /// <para>
    /// Writing a <em>slot</em> row directly is legitimate: the standing rule is that nothing but
    /// <c>TryClaimAsync</c> writes <c>BookedByUserId</c>, and this writes a free slot. The seconds
    /// component keeps it clear of the unique (RoomId, StartUtc) index, since every generated slot
    /// starts exactly on the hour.
    /// </para>
    /// </summary>
    public async Task<int> InsertFreeSlotAsync(
        int roomId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var slot = new Slot { RoomId = roomId, StartUtc = startUtc, EndUtc = endUtc };

        dbContext.Set<Slot>().Add(slot);
        await dbContext.SaveChangesAsync(cancellationToken);

        return slot.Id;
    }

    /// <summary>One booking attempt, returned unread so a test can assert on status and body.</summary>
    public async Task<HttpResponseMessage> BookAsync(string token, int slotId, CancellationToken cancellationToken)
    {
        using var client = CreateClientFor(token);

        return await client.PostAsJsonAsync("/api/bookings", new BookSlotRequest(slotId), cancellationToken);
    }

    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password), TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> LogInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password), TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);

        return login?.AccessToken ?? throw new InvalidOperationException($"No access token for {email}.");
    }

    private static async Task<RoomResponse> CreateRoomAsync(HttpClient client, string adminToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rooms")
        {
            Content = JsonContent.Create(new CreateRoomRequest($"Race Room {Guid.NewGuid():N}"[..40], 8)),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", adminToken) }
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RoomResponse>(TestContext.Current.CancellationToken)
               ?? throw new InvalidOperationException("The created room was empty.");
    }
}

/// <summary>
/// Shares one <see cref="BookingScenario"/> across every test class in it. Standing the
/// application up and registering twenty users is the slow part of a run; the race itself is
/// milliseconds.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BookingCollection : ICollectionFixture<BookingScenario>
{
    public const string Name = "booking";
}
