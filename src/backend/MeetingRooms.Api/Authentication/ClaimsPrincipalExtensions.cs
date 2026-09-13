using System.Globalization;
using System.Security.Claims;
using MeetingRooms.Application.Auth;

namespace MeetingRooms.Api.Authentication;

/// <summary>
/// Reads this application's claims off an authenticated caller.
/// <para>
/// Every accessor throws rather than returning null or a default. A request that reached an
/// authorized action has a validated token, and a validated token this application issued
/// carries all three claims - so a missing or malformed one is a broken invariant, which
/// belongs in the log as a 500 rather than being reported to the caller as their mistake.
/// </para>
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue(AppClaimTypes.Sub)
            ?? throw new InvalidOperationException(
                $"The authenticated caller has no '{AppClaimTypes.Sub}' claim.");

        return int.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
            ? userId
            : throw new InvalidOperationException(
                $"The authenticated caller's '{AppClaimTypes.Sub}' claim is not a user id.");
    }

    public static string GetEmail(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue(AppClaimTypes.Email)
            ?? throw new InvalidOperationException(
                $"The authenticated caller has no '{AppClaimTypes.Email}' claim.");
    }

    /// <summary>
    /// The caller's roles. Reads the same claim type the token was issued with, which is also
    /// what <c>TokenValidationParameters.RoleClaimType</c> is set to - so this and
    /// <c>[Authorize(Roles = ...)]</c> can never disagree about where roles live.
    /// </summary>
    public static IReadOnlyList<string> GetRoles(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return [.. principal.FindAll(AppClaimTypes.Role).Select(claim => claim.Value)];
    }
}
