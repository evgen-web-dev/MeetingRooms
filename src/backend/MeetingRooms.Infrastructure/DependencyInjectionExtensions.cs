using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Options;
using MeetingRooms.Domain.Entities;
using MeetingRooms.Infrastructure.Identity;
using MeetingRooms.Infrastructure.Persistence;
using MeetingRooms.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRooms.Infrastructure;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers the database context, ASP.NET Core Identity and the unit of work.
    /// </summary>
    public static IServiceCollection AddInfrastructurePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                // Required for Azure SQL, whose connection failures are routine rather than
                // exceptional. It is also what dictates the shape of IUnitOfWork.
                sqlServerOptions => sqlServerOptions.EnableRetryOnFailure()));

        // AddIdentityCore, never AddIdentity. AddIdentity calls AddAuthentication internally and
        // makes Identity's cookie scheme the default, which in a JWT-only API silently outranks
        // the bearer scheme: every [Authorize] endpoint starts answering with a redirect to a
        // login page that does not exist. AddIdentityCore registers UserManager, the password
        // hasher and the validators, and no authentication scheme at all.
        services.AddIdentityCore<AppUser>(options =>
            {
                // The password policy is left at Identity's defaults on purpose - see
                // docs/phases/phase-3-persistence-identity.md. These two are user options, and
                // both exist because the user name here *is* the email address.
                options.User.RequireUniqueEmail = true;

                // Identity's default allow-list is narrower than the characters an email address
                // may legally contain, so an address with an apostrophe would be rejected as an
                // invalid user name. The email itself is validated by the request validator, so
                // this check has nothing left to add.
                options.User.AllowedUserNameCharacters = string.Empty;
            })
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>();

        // Bound but not validated at startup: an absent administrator is legal in Development
        // and fatal outside it, which is a distinction ValidateOnStart cannot express. The
        // seeder decides.
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    /// <summary>
    /// Registers the implementations of the Application layer's infrastructure ports.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IUserIdentityService, UserIdentityService>();
        services.AddScoped<IRoleIdentityService, RoleIdentityService>();
        services.AddScoped<IAccessTokenService, JsonWebTokenService>();
        services.AddScoped<IDatabaseHealthProbe, DatabaseHealthProbe>();

        return services;
    }
}
