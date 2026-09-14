using System.Text.Json;
using FluentValidation;
using MeetingRooms.Api.Errors;
using MeetingRooms.Api.ExceptionHandlers;
using MeetingRooms.Api.Realtime;
using MeetingRooms.Application.Auth;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Options;
using MeetingRooms.Application.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MeetingRooms.Api;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers the global exception handler together with the ProblemDetails services it
    /// writes through. The two belong in one call: <c>UseExceptionHandler()</c> needs a
    /// registered handler, and the handler needs <see cref="IProblemDetailsService"/>.
    /// </summary>
    public static IServiceCollection AddExceptionHandlersWithProblemDetails(this IServiceCollection services)
    {
        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }

    /// <summary>
    /// Registers SignalR, backed by Azure SignalR Service when its connection string is
    /// present and running in-process otherwise.
    /// <para>
    /// The fallback is real, not decorative: the app runs as a single instance, so
    /// in-process SignalR is correct there. Clearing
    /// <c>Azure__SignalR__ConnectionString</c> is therefore a configuration change with no
    /// code change, which is the escape hatch if the service misbehaves in Azure. Locally
    /// the setting is absent because the dev container's firewall cannot reach
    /// <c>*.service.signalr.net</c>.
    /// </para>
    /// </summary>
    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var signalRBuilder = services.AddSignalR();

        // Pinned rather than inherited. SignalR's JSON protocol is configured independently of
        // MVC's, so the camelCase that every REST response uses does not carry over by
        // construction. Setting it here is what keeps one wire style across the API and the hub;
        // the failure mode otherwise is silent and only visible at runtime - `SlotId` arriving
        // where the browser reads `slotId`.
        signalRBuilder.AddJsonProtocol(options =>
            options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        // The key AddAzureSignalR() reads by default. In Azure it arrives as the app
        // setting Azure__SignalR__ConnectionString.
        var azureSignalRConnectionString = configuration["Azure:SignalR:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(azureSignalRConnectionString))
        {
            signalRBuilder.AddAzureSignalR();
        }

        // The adapter behind Application's IScheduleNotifier port, registered by the layer that
        // implements it. Singleton: it holds no state, and the IHubContext it wraps is one too.
        // BookingService is scoped and will depend on it, which is fine - a scoped service may
        // resolve a singleton; only the reverse is a lifetime bug.
        services.AddSingleton<IScheduleNotifier, SignalRScheduleNotifier>();

        return services;
    }

    /// <summary>
    /// Turns a failed use case into the API's error response.
    /// <para>
    /// The <see cref="ProblemDetails"/> is built by MVC's own
    /// <see cref="Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory"/> rather than by
    /// hand, so <c>type</c> and <c>title</c> come from the same defaults the framework uses for
    /// its automatic 400s and 404s. Hand-rolled and framework-generated failures therefore look
    /// identical to a client by construction, instead of by keeping a second table in sync.
    /// </para>
    /// </summary>
    /// <param name="controller">The calling controller, which carries both the factory and the request's <see cref="HttpContext"/>.</param>
    /// <param name="title">Overrides the framework's default title for the status code.</param>
    public static ActionResult ToProblemDetailsResult(this OperationResult result, ControllerBase controller, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        if (result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Only a failed {nameof(OperationResult)} can be turned into a problem response.");
        }

        // Errors is guaranteed non-empty: OperationResult.Failure rejects an empty list.
        var statusCode = ErrorStatusCodeMapper.GetStatusCodeForError(result.Errors[0]);

        var problemDetails = controller.ProblemDetailsFactory
            .CreateProblemDetails(controller.HttpContext, statusCode, title)
            .WithErrorCodes(result.Errors);

        return new ObjectResult(problemDetails) { StatusCode = statusCode };
    }

    /// <summary>
    /// Binds and validates <see cref="JwtOptions"/>, then configures bearer authentication from
    /// the validated values rather than by reading configuration a second time.
    /// <para>
    /// Validation runs at startup. Configuration arrives from App Service application settings
    /// in Azure, where a missing or malformed signing key would otherwise surface as a 500 on
    /// someone's first login instead of as an application that refused to start.
    /// </para>
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer),
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)} is missing.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)} is missing.")
            .Validate(options => options.AccessTokenLifetimeMinutes > 0,
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.AccessTokenLifetimeMinutes)} must be greater than zero.")
            .Validate(options => options.TryGetSigningKeyBytes(out _),
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} must be base64-encoded "
                + $"and at least {JwtOptions.MinimumSigningKeyBytes} bytes.")
            .ValidateOnStart();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                if (!jwt.TryGetSigningKeyBytes(out var signingKeyBytes))
                {
                    // Unreachable: the same check above refuses to start the application.
                    throw new InvalidOperationException(
                        $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} is unusable.");
                }

                // Without this, the legacy JWT to WS-Federation map rewrites "sub" and "role"
                // into URI claim types on the way in, so the claims read back are not the
                // claims that were issued.
                bearerOptions.MapInboundClaims = false;

                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),

                    // Pin the algorithm. Never let the token being validated choose the rules
                    // by which it is validated - that is how "alg: none" became a class of bug.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                    ValidateLifetime = true,

                    // Skew exists to absorb a difference between the issuing and validating
                    // clocks. Here they are the same process, so the five-minute default has
                    // nothing to absorb and only extends every token's real lifetime.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // The half of the role wiring that lives on the validating side. Both names
                    // come from the same constants the token is issued with.
                    NameClaimType = AppClaimTypes.Sub,
                    RoleClaimType = AppClaimTypes.Role
                };
            });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme);

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Registers every validator in this assembly. Scanned rather than listed, so adding a
    /// validator is adding a file - and a request type whose validator was never registered
    /// would otherwise pass validation silently.
    /// </summary>
    public static IServiceCollection AddAppValidation(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjectionExtensions).Assembly);

        return services;
    }
}
