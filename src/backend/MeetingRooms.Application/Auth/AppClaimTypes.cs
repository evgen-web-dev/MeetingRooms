namespace MeetingRooms.Application.Auth;

/// <summary>
/// The claim types this application issues and validates. One definition for both sides: the
/// token would still be signed and still be accepted if they disagreed, it would simply carry
/// claims nobody reads - which is how a mismatch here presents.
/// <para>
/// Short names, as registered by RFC 7519, rather than the WS-Federation URIs behind
/// <c>System.Security.Claims.ClaimTypes</c>. They live in Application so that neither the
/// issuing adapter nor the API host owns them, and so that Application needs no token library
/// to name them.
/// </para>
/// </summary>
public static class AppClaimTypes
{
    /// <summary>The subject: this application's user id.</summary>
    public const string Sub = "sub";

    public const string Email = "email";

    /// <summary>
    /// Roles. The validating side must also be told this name through
    /// <c>TokenValidationParameters.RoleClaimType</c>; left at its default,
    /// <c>[Authorize(Roles = ...)]</c> looks for a URI-typed claim, finds none, and answers 403
    /// to a token that visibly contains the right roles.
    /// </summary>
    public const string Role = "role";
}
