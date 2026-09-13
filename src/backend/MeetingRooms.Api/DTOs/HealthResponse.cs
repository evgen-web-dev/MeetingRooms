namespace MeetingRooms.Api.DTOs;

/// <param name="ConnectionStringConfigured">
/// Whether a database connection string is present - never the value itself.
/// </param>
/// <param name="DatabaseReachable">
/// Whether a connection can be opened right now. Together with the flag above it separates the
/// two ways a deployment's database configuration goes wrong: absent, or present and wrong.
/// </param>
public sealed record HealthResponse(
    string Status,
    string Environment,
    DateTime Utc,
    bool ConnectionStringConfigured,
    bool DatabaseReachable);
