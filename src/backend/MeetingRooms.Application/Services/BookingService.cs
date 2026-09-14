using MeetingRooms.Application.DTOs.Bookings;
using MeetingRooms.Application.Errors;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using MeetingRooms.Domain;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Application.Services;

/// <inheritdoc cref="IBookingService"/>
public sealed class BookingService : IBookingService
{
    private readonly ISlotRepository _slotRepository;
    private readonly TimeProvider _timeProvider;

    public BookingService(ISlotRepository slotRepository, TimeProvider timeProvider)
    {
        _slotRepository = slotRepository;
        _timeProvider = timeProvider;
    }

    public async Task<OperationResult<BookSlotResponse>> BookSlotAsync(
        BookSlotRequest request,
        int callerUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Read once and passed down, so the predicate that refuses a claim and the timestamp that
        // records one are the same instant. Truncated because the column is datetime2(0), which
        // rounds rather than truncates: an untruncated value would be sent as a parameter of that
        // same type, judging a slot ended up to half a second early and storing something the
        // response then misreports.
        var nowUtc = TruncateToSecond(_timeProvider.GetUtcNow().UtcDateTime);

        var (outcome, bookedAtUtc) =
            await _slotRepository.TryClaimAsync(request.SlotId, callerUserId, nowUtc, cancellationToken);

        return outcome switch
        {
            // Both mean the same thing to a caller: you hold this slot. The second is what makes
            // booking idempotent per user, and it is deliberate - a request replayed after its own
            // commit must not be told it lost. The time reported is the one on the row, so asking
            // twice does not move it. See docs/decisions.md.
            SlotClaimOutcome.Claimed or SlotClaimOutcome.AlreadyClaimedByCaller =>
                OperationResult<BookSlotResponse>.Success(
                    new BookSlotResponse(
                        request.SlotId,
                        bookedAtUtc ?? throw new InvalidOperationException(
                            $"Outcome {outcome} says the caller holds slot {request.SlotId}, but carries no booking time."))),

            SlotClaimOutcome.AlreadyBooked =>
                OperationResult<BookSlotResponse>.Failure(BookingErrorCodes.SlotAlreadyBooked),

            SlotClaimOutcome.HasEnded =>
                OperationResult<BookSlotResponse>.Failure(BookingErrorCodes.SlotHasEnded),

            SlotClaimOutcome.NotFound =>
                OperationResult<BookSlotResponse>.Failure(BookingErrorCodes.SlotNotFound),

            // Unreachable through the enum's declared members. It exists because a C# switch
            // expression over an enum is not exhaustive - any int can be cast to one - and a
            // silent fall-through here would report a refused booking as a success.
            _ => throw new ArgumentOutOfRangeException(nameof(request), outcome, "Unhandled claim outcome.")
        };
    }

    public async Task<MyBookingsResponse> GetMyBookingsAsync(
        int callerUserId,
        CancellationToken cancellationToken)
    {
        var slots = await _slotRepository.ListBookedForUserAsync(callerUserId, cancellationToken);

        return new MyBookingsResponse(
            AppTimeZone.IanaId,
            [.. slots.Select(slot => new MyBookingResponse(
                slot.Id,
                slot.RoomId,
                RequireRoom(slot).Name,
                slot.StartUtc,
                slot.EndUtc,
                RequireBookedAt(slot)))]);
    }

    public async Task<AllBookingsResponse> GetAllBookingsAsync(CancellationToken cancellationToken)
    {
        var slots = await _slotRepository.ListAllBookedAsync(cancellationToken);

        return new AllBookingsResponse(
            AppTimeZone.IanaId,
            [.. slots.Select(slot => new AdminBookingResponse(
                slot.Id,
                slot.RoomId,
                RequireRoom(slot).Name,
                slot.StartUtc,
                slot.EndUtc,
                RequireBookedAt(slot),
                RequireBookerEmail(slot)))]);
    }

    /// <summary>
    /// Drops everything below whole seconds, preserving <see cref="DateTime.Kind"/>. There is no
    /// BCL helper for this; the ticks arithmetic is the idiom.
    /// </summary>
    private static DateTime TruncateToSecond(DateTime instant) =>
        new(instant.Ticks - (instant.Ticks % TimeSpan.TicksPerSecond), instant.Kind);

    // The three helpers below turn "the repository did not load what this projection needs" into a
    // named 500 rather than a bare null-reference somewhere inside a collection expression. None
    // of them is a business failure: every one is a broken invariant of the read that produced the
    // slot, and TryClaimAsync writes the booker and the booking time together or not at all.
    private static Room RequireRoom(Slot slot) =>
        slot.Room ?? throw new InvalidOperationException($"Slot {slot.Id} was read without its room.");

    private static DateTime RequireBookedAt(Slot slot) =>
        slot.BookedAtUtc ?? throw new InvalidOperationException(
            $"Slot {slot.Id} has a booker but no booking time.");

    private static string RequireBookerEmail(Slot slot) =>
        (slot.BookedByUser ?? throw new InvalidOperationException(
            $"Slot {slot.Id} was read without its booker.")).Email
        ?? throw new InvalidOperationException(
            $"The user who booked slot {slot.Id} has no email address.");
}
