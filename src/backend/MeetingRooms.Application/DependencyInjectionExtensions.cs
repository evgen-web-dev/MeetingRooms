using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRooms.Application;

public static class DependencyInjectionExtensions
{
    /// <summary>Registers the use cases. Their ports are registered by the layers that implement them.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRoomService, RoomService>();

        return services;
    }
}
