using FluentValidation;
using MeetingRooms.Application.DTOs.Rooms;
using MeetingRooms.Domain;

namespace MeetingRooms.Api.Validators.Rooms;

/// <summary>
/// Both bounds are optional - absent means "the whole current window", which the service
/// resolves. These rules therefore only apply when a caller supplied both.
/// </summary>
public sealed class ScheduleRangeRequestValidator : AbstractValidator<ScheduleRangeRequest>
{
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(SlotGrid.HorizonDays);

    public ScheduleRangeRequestValidator()
    {
        RuleFor(request => request.ToUtc)
            .Cascade(CascadeMode.Stop)
            .Must((request, toUtc) => toUtc > request.FromUtc)
                .WithMessage("'toUtc' must be later than 'fromUtc'.")
            .Must((request, toUtc) => toUtc - request.FromUtc <= MaxRange)
                .WithMessage($"The requested range must not exceed {SlotGrid.HorizonDays} days.")
            .When(request => request.FromUtc.HasValue && request.ToUtc.HasValue);
    }
}
