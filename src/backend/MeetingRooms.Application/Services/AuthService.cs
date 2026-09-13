using MeetingRooms.Application.DTOs.Auth;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using MeetingRooms.Domain;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Application.Services;

/// <inheritdoc cref="IAuthService"/>
public sealed class AuthService : IAuthService
{
    private readonly IUserIdentityService _userIdentityService;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IUnitOfWork _unitOfWork;

    public AuthService(
        IUserIdentityService userIdentityService,
        IAccessTokenService accessTokenService,
        IUnitOfWork unitOfWork)
    {
        _userIdentityService = userIdentityService;
        _accessTokenService = accessTokenService;
        _unitOfWork = unitOfWork;
    }

    public async Task<OperationResult<RegisterResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validators reject, services transform: a validator returns a verdict, never a
        // normalised value, so trimming belongs here.
        var email = request.Email.Trim();

        // Two writes - the user row and its role - so they need to be one transaction. Without
        // it, a role assignment that fails leaves a user who can log in and is authorised for
        // nothing. The unit of work commits only when the returned result succeeded, so the
        // early return below rolls back exactly as a thrown exception would.
        return await _unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            // The user name is the email address: registration is email + password, and
            // Identity requires a user name regardless.
            var user = new AppUser { UserName = email, Email = email };

            var createResult = await _userIdentityService.CreateAsync(user, request.Password);

            if (!createResult.Succeeded)
            {
                return OperationResult<RegisterResponse>.Failure(createResult.Errors);
            }

            // Always User. There is no role choice on registration and no promote-to-admin
            // flow; the single admin is seeded from configuration.
            var addToRoleResult = await _userIdentityService.AddToRoleAsync(user, Roles.User);

            if (!addToRoleResult.Succeeded)
            {
                return OperationResult<RegisterResponse>.Failure(addToRoleResult.Errors);
            }

            return OperationResult<RegisterResponse>.Success(new RegisterResponse(createResult.Value, email));
        }, cancellationToken);
    }

    public async Task<OperationResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var authenticateResult = await _userIdentityService.AuthenticateAsync(
            request.Email.Trim(),
            request.Password);

        if (!authenticateResult.Succeeded)
        {
            return OperationResult<LoginResponse>.Failure(authenticateResult.Errors);
        }

        // No transaction and no write: login issues a token and records nothing. That is what
        // "no refresh tokens" buys - there is no server-side session to persist.
        var issuedAccessToken = _accessTokenService.Issue(authenticateResult.Value);

        return OperationResult<LoginResponse>.Success(
            new LoginResponse(issuedAccessToken.AccessToken, issuedAccessToken.ExpiresAtUtc));
    }
}
