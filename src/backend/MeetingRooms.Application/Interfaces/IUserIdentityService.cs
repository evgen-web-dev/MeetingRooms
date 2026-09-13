using MeetingRooms.Application.DTOs.Auth;
using MeetingRooms.Application.Results;
using MeetingRooms.Domain.Entities;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// What this application needs from ASP.NET Core Identity's user store, and nothing else.
/// <c>UserManager&lt;AppUser&gt;</c> exposes roughly sixty members and never appears above
/// Infrastructure; the five below are shaped around the callers, not around the library.
/// <para>
/// No <c>CancellationToken</c> parameters, deliberately: <c>UserManager</c>'s own methods do
/// not accept one, so a token here would be a promise the implementation cannot keep.
/// </para>
/// </summary>
public interface IUserIdentityService
{
    /// <summary>Creates a user with a password, returning the new user's id.</summary>
    Task<OperationResult<int>> CreateAsync(AppUser user, string password);

    /// <summary>
    /// Adds a user to a role. Worth knowing: this re-runs user validation rather than only
    /// writing the join row, so email and user-name error codes can come back from it.
    /// </summary>
    Task<OperationResult> AddToRoleAsync(AppUser user, string roleName);

    /// <summary>
    /// Whether a user already holds a role. Exists so the admin seeder can be idempotent by
    /// asking, rather than by calling <see cref="AddToRoleAsync"/> and interpreting the error
    /// code it gets back.
    /// </summary>
    Task<bool> IsInRoleAsync(AppUser user, string roleName);

    /// <summary>
    /// Checks credentials. Every failure - unknown email, wrong password - returns the same
    /// single error code.
    /// </summary>
    Task<OperationResult<AuthenticatedUser>> AuthenticateAsync(string email, string password);

    /// <summary>
    /// Looks a user up without checking credentials. Exists for the admin seeder, which must
    /// be able to tell "already seeded" from "create it now" without guessing.
    /// </summary>
    Task<AppUser?> FindByEmailAsync(string email);
}
