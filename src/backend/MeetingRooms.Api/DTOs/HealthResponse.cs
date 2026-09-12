namespace MeetingRooms.Api.DTOs;

/// <param name="ConnectionStringConfigured">
/// Whether a database connection string is present - never the value itself.
/// </param>
public sealed record HealthResponse(
    string Status,
    string Environment,
    DateTime Utc,
    bool ConnectionStringConfigured);
