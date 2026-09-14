using MeetingRooms.Application.DTOs.Bookings;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using MeetingRooms.Application.Services;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.UnitTests;

/// <summary>
/// Which claim outcomes announce a change, and which do not.
/// <para>
/// This exists because the obvious implementation is wrong. <c>Claimed</c> and
/// <c>AlreadyClaimedByCaller</c> are both a <em>successful</em> <see cref="OperationResult"/>, so
/// "announce whenever the booking succeeded" compiles, passes every HTTP test in the solution, and
/// tells every viewer of a room that something happened each time a caller re-posts a booking they
/// already hold. Nothing else in the suite would notice.
/// </para>
/// <para>
/// Unit tests rather than an end-to-end hub test: the rule lives entirely in
/// <see cref="BookingService"/>, and hand-written fakes reach it without a SignalR client, a test
/// server or a clock. <em>Probe:</em> delete the <c>if (outcome is SlotClaimOutcome.Claimed)</c>
/// guard and <see cref="ASlotTheCallerAlreadyHeldIsAnnouncedToNobody"/> must go red.
/// </para>
/// </summary>
public sealed class BookingServiceNotificationTests
{
    private const int SlotId = 7;
    private const int RoomId = 42;
    private const int CallerUserId = 3;

    [Fact]
    public async Task AClaimedSlotIsAnnouncedOnceToItsRoom()
    {
        var (service, slots, notifier) = Build(SlotClaimOutcome.Claimed, RoomId);

        var result = await service.BookSlotAsync(
            new BookSlotRequest(SlotId), CallerUserId, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);

        // Once, not at-least-once: a duplicate announcement would redraw the slot twice and would
        // be invisible to any assertion that only checked "something was sent".
        var announcement = Assert.Single(notifier.Announcements);
        Assert.Equal((RoomId, SlotId), announcement);

        // The room is read from the slot, never taken from the request.
        Assert.Equal(1, slots.RoomIdReads);
    }

    /// <summary>
    /// The phase 5 carry, and the whole point of this class. A caller re-posting a booking they
    /// already hold is answered 201 by design - but the row did not change, so there is nothing to
    /// announce.
    /// </summary>
    [Fact]
    public async Task ASlotTheCallerAlreadyHeldIsAnnouncedToNobody()
    {
        var (service, slots, notifier) = Build(SlotClaimOutcome.AlreadyClaimedByCaller, RoomId);

        var result = await service.BookSlotAsync(
            new BookSlotRequest(SlotId), CallerUserId, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(notifier.Announcements);

        // Not even looked up: the guard short-circuits before the read, so a replay costs no
        // database round trip either.
        Assert.Equal(0, slots.RoomIdReads);
    }

    [Theory]
    [InlineData(SlotClaimOutcome.AlreadyBooked)]
    [InlineData(SlotClaimOutcome.HasEnded)]
    [InlineData(SlotClaimOutcome.NotFound)]
    public async Task ARefusedClaimIsAnnouncedToNobody(SlotClaimOutcome outcome)
    {
        var (service, slots, notifier) = Build(outcome, RoomId);

        var result = await service.BookSlotAsync(
            new BookSlotRequest(SlotId), CallerUserId, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Empty(notifier.Announcements);
        Assert.Equal(0, slots.RoomIdReads);
    }

    /// <summary>
    /// The deliberately unreachable branch, pinned because its handling is a decision rather than
    /// an oversight: the claim has committed and cannot be undone, so a room id that cannot be read
    /// skips the announcement instead of throwing. Throwing would answer the one caller who did get
    /// the room by telling them they did not.
    /// </summary>
    [Fact]
    public async Task ASlotWhoseRoomCannotBeReadIsStillBooked()
    {
        var (service, _, notifier) = Build(SlotClaimOutcome.Claimed, roomId: null);

        var result = await service.BookSlotAsync(
            new BookSlotRequest(SlotId), CallerUserId, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(notifier.Announcements);
    }

    private static (BookingService Service, StubSlotRepository Slots, RecordingScheduleNotifier Notifier) Build(
        SlotClaimOutcome outcome,
        int? roomId)
    {
        var slots = new StubSlotRepository(outcome, roomId);
        var notifier = new RecordingScheduleNotifier();

        return (new BookingService(slots, notifier, TimeProvider.System), slots, notifier);
    }

    /// <summary>
    /// Reports the outcome it was built with. Hand-written rather than a mocking package: the
    /// interface has five members and only two of them matter here.
    /// </summary>
    private sealed class StubSlotRepository : ISlotRepository
    {
        private readonly SlotClaimOutcome _outcome;
        private readonly int? _roomId;

        public StubSlotRepository(SlotClaimOutcome outcome, int? roomId)
        {
            _outcome = outcome;
            _roomId = roomId;
        }

        public int RoomIdReads { get; private set; }

        public Task<(SlotClaimOutcome Outcome, DateTime? BookedAtUtc)> TryClaimAsync(
            int slotId,
            int userId,
            DateTime nowUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult((
                _outcome,
                _outcome is SlotClaimOutcome.Claimed or SlotClaimOutcome.AlreadyClaimedByCaller
                    ? (DateTime?)nowUtc
                    : null));

        public Task<int?> GetRoomIdAsync(int slotId, CancellationToken cancellationToken)
        {
            RoomIdReads++;

            return Task.FromResult(_roomId);
        }

        // The three reads this rule does not touch. They throw rather than returning an empty
        // list: a test that quietly took a path nobody meant to exercise is worse than one that
        // fails saying so.
        public Task<IReadOnlyList<Slot>> ListForRoomAsync(
            int roomId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Slot>> ListBookedForUserAsync(int userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Slot>> ListAllBookedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Records what was announced, in order, instead of sending it anywhere.</summary>
    private sealed class RecordingScheduleNotifier : IScheduleNotifier
    {
        public List<(int RoomId, int SlotId)> Announcements { get; } = [];

        public Task SlotBookedAsync(int roomId, int slotId)
        {
            Announcements.Add((roomId, slotId));

            return Task.CompletedTask;
        }
    }
}
