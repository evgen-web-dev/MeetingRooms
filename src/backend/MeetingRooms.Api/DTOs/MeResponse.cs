namespace MeetingRooms.Api.DTOs;

/// <summary>
/// The caller, as their own access token describes them. It lives in the API layer rather than
/// in Application because no use case produces it: the values are read straight off the
/// validated token, and the endpoint never reaches the database.
/// </summary>
public sealed record MeResponse(int Id, string Email, IReadOnlyList<string> Roles);
