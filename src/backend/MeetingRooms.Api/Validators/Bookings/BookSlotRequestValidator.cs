using FluentValidation;
using MeetingRooms.Application.DTOs.Bookings;

namespace MeetingRooms.Api.Validators.Bookings;

/// <summary>
/// The entire payload-only surface of a booking request, and it is one rule.
/// <para>
/// Whether the slot exists, is still free, or has already ended each needs a database read, so by
/// the rule in <c>docs/decisions.md</c> none of them belongs here: they are business error codes
/// reaching the caller as a 404 or a 409, not field validation reaching them as a 400.
/// </para>
/// </summary>
public sealed class BookSlotRequestValidator : AbstractValidator<BookSlotRequest>
{
    public BookSlotRequestValidator()
    {
        // Identity values start at 1, so a zero or negative id cannot match a row and the database
        // has nothing to say about it. The rule earns its place on the omitted-field case: a body
        // with no slotId binds to 0, and this turns that into a 400 naming the field rather than a
        // 404 implying the slot simply does not exist yet.
        RuleFor(request => request.SlotId).GreaterThan(0);
    }
}
