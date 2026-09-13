using MeetingRooms.Application.Results;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// What this application needs from Identity's role store. Only the seeder uses it: roles are
/// reference data created at startup, never through the API.
/// </summary>
public interface IRoleIdentityService
{
    Task<bool> ExistsAsync(string roleName);

    Task<OperationResult> CreateAsync(string roleName);
}
