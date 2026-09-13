using MeetingRooms.Application.DTOs.Auth;
using MeetingRooms.Application.Results;

namespace MeetingRooms.Application.Interfaces;

/// <summary>Registration and login, as the API sees them.</summary>
public interface IAuthService
{
    /// <summary>
    /// Creates a user and assigns it the <c>User</c> role. There is no role choice: the single
    /// admin is seeded from configuration and there is no promote-to-admin flow.
    /// </summary>
    Task<OperationResult<RegisterResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);
}
