using MeetingRooms.Api.Authentication;
using MeetingRooms.Application.DTOs.Rooms;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Controllers;

/// <summary>
/// Rooms, and the schedule of one room.
/// <para>
/// Reading is open to any authenticated caller; creating, editing and deleting are the
/// administrator's. This is the first place in the application where a role actually gates
/// something - phase 3 proved the wiring with a temporary attribute and then reverted it.
/// </para>
/// </summary>
[ApiController]
[Route("api/rooms")]
public sealed class RoomsController : ControllerBase
{
    private readonly IRoomService _roomService;

    public RoomsController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    [Authorize]
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoomResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<RoomResponse>>> ListRooms(CancellationToken cancellationToken) =>
        Ok(await _roomService.ListRoomsAsync(cancellationToken));

    /// <summary>
    /// One room. Exists as much for <c>CreatedAtAction</c> below to point at as for its own sake -
    /// a 201 should say where the thing it made now lives.
    /// </summary>
    [Authorize]
    [HttpGet("{id:int}", Name = nameof(GetRoom))]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomResponse>> GetRoom(int id, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetRoomAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToProblemDetailsResult(this);
    }

    /// <summary>Creates the room and its grid in one write.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RoomResponse>> CreateRoom(
        [FromBody] CreateRoomRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _roomService.CreateRoomAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            return result.ToProblemDetailsResult(this);
        }

        return CreatedAtAction(nameof(GetRoom), new { id = result.Value.Id }, result.Value);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:int}")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomResponse>> UpdateRoom(
        int id,
        [FromBody] UpdateRoomRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _roomService.UpdateRoomAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToProblemDetailsResult(this);
    }

    /// <summary>
    /// Deletes the room and, through the database's cascade, its slots - unless one of them is
    /// booked, which is a 409. Bookings cannot be cancelled, so a room cannot be deleted out from
    /// under one.
    /// </summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteRoom(int id, CancellationToken cancellationToken)
    {
        var result = await _roomService.DeleteRoomAsync(id, cancellationToken);

        return result.Succeeded ? NoContent() : result.ToProblemDetailsResult(this);
    }

    /// <summary>
    /// One room's grid. Both bounds are optional; omitting them returns the whole current window.
    /// Times are UTC instants, and the response names the zone they are meant to be read in.
    /// </summary>
    [Authorize]
    [HttpGet("{id:int}/schedule")]
    [ProducesResponseType<ScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleResponse>> GetSchedule(
        int id,
        [FromQuery] ScheduleRangeRequest range,
        CancellationToken cancellationToken)
    {
        var result = await _roomService.GetScheduleAsync(id, range, User.GetUserId(), cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToProblemDetailsResult(this);
    }
}
