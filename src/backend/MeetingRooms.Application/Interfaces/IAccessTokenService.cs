using MeetingRooms.Application.DTOs.Auth;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Issues access tokens. A port rather than a direct call so that Application never references
/// a token library, and so the shape of a token stays one implementation's business.
/// </summary>
public interface IAccessTokenService
{
    IssuedAccessToken Issue(AuthenticatedUser user);
}
