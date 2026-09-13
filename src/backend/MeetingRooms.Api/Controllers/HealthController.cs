using MeetingRooms.Api.DTOs;
using MeetingRooms.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Controllers;

/// <summary>
/// Liveness probe. Its route is "/health" rather than "/api/health" because the deployed
/// app and the Azure smoke test already use that URL; changing it would break something
/// outside this repository for no gain.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;
    private readonly IDatabaseHealthProbe _databaseHealthProbe;

    public HealthController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        TimeProvider timeProvider,
        IDatabaseHealthProbe databaseHealthProbe)
    {
        _configuration = configuration;
        _environment = environment;
        _timeProvider = timeProvider;
        _databaseHealthProbe = databaseHealthProbe;
    }

    [HttpGet]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        // Report only whether the connection string is present.
        // Never return or log the value itself.
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        return Ok(new HealthResponse(
            Status: "ok",
            Environment: _environment.EnvironmentName,
            Utc: _timeProvider.GetUtcNow().UtcDateTime,
            ConnectionStringConfigured: !string.IsNullOrWhiteSpace(connectionString),
            DatabaseReachable: await _databaseHealthProbe.CanConnectAsync(cancellationToken)));
    }
}
