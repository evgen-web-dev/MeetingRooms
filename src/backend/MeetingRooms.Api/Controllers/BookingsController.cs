using MeetingRooms.Api.Authentication;
using MeetingRooms.Application.DTOs.Bookings;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Controllers;

/// <summary>
/// Claiming a slot, and reading back what has been claimed.
/// <para>
/// The route is written out rather than assembled from the <c>[controller]</c> token, so that
/// renaming this class cannot change a URL a client depends on.
/// </para>
/// </summary>
[ApiController]
[Route("api/bookings")]
public sealed class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    /// <summary>
    /// Books one slot. When several callers request the same slot at once, exactly one receives
    /// this 201 and the rest receive 409 <c>SlotAlreadyBooked</c> - never a silent overwrite and
    /// never a 5xx.
    /// </summary>
    /// <remarks>
    /// The body carries a server-generated slot id and no time at all, which is what makes the
    /// booking key immune to clock skew and to <c>datetime2</c> precision.
    /// <para>
    /// A caller who already holds the slot also receives 201, carrying the booking time from
    /// their original request rather than this one. That is deliberate: a request replayed after
    /// its own commit must not be told it lost. A separate 200 would force every client to branch
    /// on a distinction that means nothing to them - either way, they hold the slot.
    /// </para>
    /// </remarks>
    [Authorize]
    [HttpPost]
    [ProducesResponseType<BookSlotResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookSlotResponse>> BookSlot(
        [FromBody] BookSlotRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.BookSlotAsync(request, User.GetUserId(), cancellationToken);

        if (!result.Succeeded)
        {
            return result.ToProblemDetailsResult(this);
        }

        // 201 with no Location header, for the reason registration already has one: a booking is
        // not an addressable resource here - it has no identity of its own - so the only thing to
        // point at would be the slot, which is not what was created.
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>The caller's own bookings, earliest slot first.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<MyBookingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MyBookingsResponse>> GetMyBookings(CancellationToken cancellationToken) =>
        Ok(await _bookingService.GetMyBookingsAsync(User.GetUserId(), cancellationToken));

    /// <summary>
    /// Every booking across every user - the administrator capability the assignment asks for,
    /// and the only read in this application that discloses one user's identity to another.
    /// </summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpGet]
    [ProducesResponseType<AllBookingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AllBookingsResponse>> GetAllBookings(CancellationToken cancellationToken) =>
        Ok(await _bookingService.GetAllBookingsAsync(cancellationToken));
}
