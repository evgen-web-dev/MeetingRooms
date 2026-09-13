using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Options;
using MeetingRooms.Domain;
using MeetingRooms.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeetingRooms.Infrastructure.Seeders;

/// <summary>
/// Puts the identity data the application cannot run without into the database: the roles, and
/// the one administrator.
/// <para>
/// Both seeders are idempotent behind a cheap existence check, because they run on every
/// start, and both throw with a reason rather than leaving a half-seeded database - a role that
/// silently failed to appear would surface much later as a 403 nobody can explain.
/// </para>
/// </summary>
public static class IdentitySeeder
{
    /// <summary>
    /// Creates any missing role. Runs in every environment: roles are reference data, not demo
    /// data, and registration cannot complete without one.
    /// </summary>
    public static async Task SeedRolesAsync(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        var roleIdentityService = scopedServices.GetRequiredService<IRoleIdentityService>();

        foreach (var roleName in Roles.AllRoles)
        {
            if (await roleIdentityService.ExistsAsync(roleName))
            {
                continue;
            }

            var createRoleResult = await roleIdentityService.CreateAsync(roleName);

            if (!createRoleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed the role '{roleName}': {string.Join(", ", createRoleResult.Errors)}.");
            }
        }
    }

    /// <summary>
    /// Creates the administrator from configuration, or grants an existing account the role.
    /// <para>
    /// Missing credentials are a warning in Development and a hard failure everywhere else. A
    /// developer without them gets a working application minus the admin account; a deployment
    /// without them would silently have no administrator at all, which is worth refusing to
    /// start over.
    /// </para>
    /// </summary>
    public static async Task SeedAdminAsync(IServiceProvider scopedServices, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        var seedOptions = scopedServices.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = scopedServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        if (string.IsNullOrWhiteSpace(seedOptions.AdminEmail) || string.IsNullOrWhiteSpace(seedOptions.AdminPassword))
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    $"{SeedOptions.SectionName}:{nameof(SeedOptions.AdminEmail)} and "
                    + $"{SeedOptions.SectionName}:{nameof(SeedOptions.AdminPassword)} are required "
                    + "outside Development, where they come from application settings.");
            }

            // Deliberately says where to put them and never what they are.
            logger.LogWarning(
                "No administrator configured, so none was seeded. Set {EmailKey} and {PasswordKey} in user-secrets.",
                $"{SeedOptions.SectionName}:{nameof(SeedOptions.AdminEmail)}",
                $"{SeedOptions.SectionName}:{nameof(SeedOptions.AdminPassword)}");

            return;
        }

        var userIdentityService = scopedServices.GetRequiredService<IUserIdentityService>();
        var admin = await userIdentityService.FindByEmailAsync(seedOptions.AdminEmail);

        if (admin is null)
        {
            admin = new AppUser { UserName = seedOptions.AdminEmail, Email = seedOptions.AdminEmail };

            var createAdminResult = await userIdentityService.CreateAsync(admin, seedOptions.AdminPassword);

            if (!createAdminResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed the administrator: {string.Join(", ", createAdminResult.Errors)}.");
            }

            logger.LogInformation("Seeded the administrator account.");
        }

        // An existing account keeps its password. Re-applying the configured one on every start
        // would quietly undo a password change, and would make the configured value the real
        // credential forever.
        if (await userIdentityService.IsInRoleAsync(admin, Roles.Admin))
        {
            return;
        }

        var addToRoleResult = await userIdentityService.AddToRoleAsync(admin, Roles.Admin);

        if (!addToRoleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to grant the administrator the '{Roles.Admin}' role: "
                + $"{string.Join(", ", addToRoleResult.Errors)}.");
        }
    }
}
