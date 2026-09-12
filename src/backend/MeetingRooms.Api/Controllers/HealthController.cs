using MeetingRooms.Api.DTOs;
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

    public HealthController(IConfiguration configuration, IWebHostEnvironment environment, TimeProvider timeProvider)
    {
        _configuration = configuration;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        // Report only whether the connection string is present.
        // Never return or log the value itself.
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        return Ok(new HealthResponse(
            Status: "ok",
            Environment: _environment.EnvironmentName,
            Utc: _timeProvider.GetUtcNow().UtcDateTime,
            ConnectionStringConfigured: !string.IsNullOrWhiteSpace(connectionString)));
    }
}
