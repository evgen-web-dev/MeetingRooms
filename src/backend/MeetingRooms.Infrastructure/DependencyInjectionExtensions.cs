using MeetingRooms.Application.Interfaces;
using MeetingRooms.Domain.Entities;
using MeetingRooms.Infrastructure.Persistence;
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
        services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
