using FluentValidation;
using MeetingRooms.Application.DTOs.Auth;

namespace MeetingRooms.Api.Validators.Auth;

/// <summary>
/// The same payload-only rules as registration. Credentials that are well formed but wrong are
/// not a validation failure - they are a 401 carrying one error code, decided by the service.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    private const int EmailMaxLength = 256;

    private const int PasswordMaxLength = 128;

    public LoginRequestValidator()
    {
        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(EmailMaxLength)
            .EmailAddress();

        // Capped here too, even though login stores nothing: the password is hashed before it
        // can be rejected, so an unbounded one is work an unauthenticated caller can demand.
        RuleFor(request => request.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(PasswordMaxLength);
    }
}
