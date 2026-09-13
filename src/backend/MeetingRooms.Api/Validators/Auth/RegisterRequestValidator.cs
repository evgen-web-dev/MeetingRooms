using FluentValidation;
using MeetingRooms.Application.DTOs.Auth;

namespace MeetingRooms.Api.Validators.Auth;

/// <summary>
/// Payload-only rules. A check belongs here when it can be settled from the request alone,
/// needs no database read, and naming the field it failed on leaks nothing - everything else is
/// a business error code from the service.
/// <para>
/// The password *policy* is deliberately absent: it is Identity's, and restating it here would
/// mean two sets of rules to keep in agreement. The only password rule below is a length cap,
/// which is not policy - it is a bound on how much work a request can make the server do.
/// </para>
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    /// <summary>Matches the nvarchar(256) Identity stores an email in.</summary>
    private const int EmailMaxLength = 256;

    private const int PasswordMaxLength = 128;

    public RegisterRequestValidator()
    {
        // Cascade(Stop) so the rules below can assume the ones above passed - MaximumLength and
        // EmailAddress never see the empty string NotEmpty already rejected.
        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(EmailMaxLength)
            .EmailAddress();

        RuleFor(request => request.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(PasswordMaxLength);
    }
}
