using FluentValidation;
using MeetingRooms.Application.DTOs.Rooms;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Api.Validators.Rooms;

/// <summary>
/// Payload-only rules, per <c>docs/decisions.md</c>: everything here is settled from the request
/// alone, needs no database read, and naming the field it failed on leaks nothing.
/// </summary>
public sealed class CreateRoomRequestValidator : AbstractValidator<CreateRoomRequest>
{
    /// <summary>
    /// A bound rather than a business rule. Nothing depends on the ceiling; it exists so a typo
    /// of 100000 is rejected as input instead of becoming a room nobody can fill.
    /// </summary>
    public const int MaxCapacity = 1000;

    public CreateRoomRequestValidator()
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(Room.NameMaxLength);

        RuleFor(request => request.Capacity)
            .InclusiveBetween(1, MaxCapacity);
    }
}
