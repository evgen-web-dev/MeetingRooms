using FluentValidation;
using MeetingRooms.Application.DTOs.Rooms;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Api.Validators.Rooms;

/// <summary>
/// The same rules as <see cref="CreateRoomRequestValidator"/>, restated rather than shared.
/// <para>
/// FluentValidation's <c>Include</c> only composes validators of the same type, and the
/// validation filter resolves by <em>concrete</em> type - so a request type without its own
/// validator is not validated at all, silently. Two short rule sets that are free to diverge beat
/// one shared type that has to be untangled the first time they do.
/// </para>
/// </summary>
public sealed class UpdateRoomRequestValidator : AbstractValidator<UpdateRoomRequest>
{
    public UpdateRoomRequestValidator()
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(Room.NameMaxLength);

        RuleFor(request => request.Capacity)
            .InclusiveBetween(1, CreateRoomRequestValidator.MaxCapacity);
    }
}
