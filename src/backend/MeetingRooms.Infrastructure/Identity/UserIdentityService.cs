using MeetingRooms.Application.DTOs.Auth;
using MeetingRooms.Application.Errors;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using MeetingRooms.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace MeetingRooms.Infrastructure.Identity;

/// <inheritdoc cref="IUserIdentityService"/>
public sealed class UserIdentityService : IUserIdentityService
{
    private readonly UserManager<AppUser> _userManager;

    public UserIdentityService(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<OperationResult<int>> CreateAsync(AppUser user, string password)
    {
        var createResult = await _userManager.CreateAsync(user, password);

        // UserManager assigns the key as part of the insert, so user.Id is only meaningful
        // once this has succeeded.
        return createResult.Succeeded
            ? OperationResult<int>.Success(user.Id)
            : OperationResult<int>.Failure(
                IdentityErrorCodesDefaultDenyMapper.ForUserCreation(createResult.Errors));
    }

    public async Task<OperationResult> AddToRoleAsync(AppUser user, string roleName)
    {
        var addToRoleResult = await _userManager.AddToRoleAsync(user, roleName);

        return addToRoleResult.Succeeded
            ? OperationResult.Success()
            : OperationResult.Failure(
                IdentityErrorCodesDefaultDenyMapper.ForAddToRole(addToRoleResult.Errors));
    }

    public Task<bool> IsInRoleAsync(AppUser user, string roleName) =>
        _userManager.IsInRoleAsync(user, roleName);

    public async Task<OperationResult<AuthenticatedUser>> AuthenticateAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Hash the submitted password against nothing, so that an unknown address costs
            // roughly what a wrong password costs. Returning one error code for both failures
            // hides which one occurred; without this, the response time would answer it anyway.
            // A mitigation, not a guarantee - the work is comparable, not identical.
            _userManager.PasswordHasher.HashPassword(new AppUser(), password);

            return OperationResult<AuthenticatedUser>.Failure(AuthErrorCodes.InvalidEmailOrPassword);
        }

        if (!await _userManager.CheckPasswordAsync(user, password))
        {
            return OperationResult<AuthenticatedUser>.Failure(AuthErrorCodes.InvalidEmailOrPassword);
        }

        var roles = await _userManager.GetRolesAsync(user);

        return OperationResult<AuthenticatedUser>.Success(
            new AuthenticatedUser(user.Id, RequireEmail(user), [.. roles]));
    }

    public Task<AppUser?> FindByEmailAsync(string email) => _userManager.FindByEmailAsync(email);

    /// <summary>
    /// Every path that creates a user sets an email, so a user without one is a broken
    /// invariant rather than a request that failed. It throws for the same reason the global
    /// handler exists: an unreachable state should be loud, not quietly returned as a 400.
    /// </summary>
    private static string RequireEmail(AppUser user) =>
        user.Email
        ?? throw new InvalidOperationException(
            $"User {user.Id} has no email address, which registration and seeding both set.");
}
