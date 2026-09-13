using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using Microsoft.AspNetCore.Identity;

namespace MeetingRooms.Infrastructure.Identity;

/// <inheritdoc cref="IRoleIdentityService"/>
public sealed class RoleIdentityService : IRoleIdentityService
{
    private readonly RoleManager<IdentityRole<int>> _roleManager;

    public RoleIdentityService(RoleManager<IdentityRole<int>> roleManager)
    {
        _roleManager = roleManager;
    }

    public Task<bool> ExistsAsync(string roleName) => _roleManager.RoleExistsAsync(roleName);

    public async Task<OperationResult> CreateAsync(string roleName)
    {
        var createResult = await _roleManager.CreateAsync(new IdentityRole<int> { Name = roleName });

        return createResult.Succeeded
            ? OperationResult.Success()
            : OperationResult.Failure(
                IdentityErrorCodesDefaultDenyMapper.ForRoleCreation(createResult.Errors));
    }
}
