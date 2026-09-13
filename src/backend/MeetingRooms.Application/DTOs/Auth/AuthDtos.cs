namespace MeetingRooms.Application.DTOs.Auth;

/// <param name="Email">Also becomes the user name: registration is email + password only.</param>
public sealed record RegisterRequest(string Email, string Password);

public sealed record RegisterResponse(int Id, string Email);

public sealed record LoginRequest(string Email, string Password);

/// <param name="ExpiresAtUtc">
/// So a client can see a session end coming rather than discovering it on the next 401. There
/// are no refresh tokens by decision, so this is the end of the session, not of a token.
/// </param>
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// A user whose credentials have just been checked, carrying exactly what goes into an access
/// token. Not an entity: nothing below this type reaches the database again.
/// </summary>
public sealed record AuthenticatedUser(int Id, string Email, IReadOnlyList<string> Roles);

/// <summary>An issued access token and the instant it stops being accepted.</summary>
public sealed record IssuedAccessToken(string AccessToken, DateTime ExpiresAtUtc);
