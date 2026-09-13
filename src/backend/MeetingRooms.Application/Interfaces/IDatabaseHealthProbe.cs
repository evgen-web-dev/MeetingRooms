namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Answers whether the database is reachable right now. A port rather than a database context
/// injected into a controller, because <c>AppDbContext</c> stays behind Infrastructure
/// (<c>docs/decisions.md</c>) - a diagnostic is not a reason to make an exception to that.
/// </summary>
public interface IDatabaseHealthProbe
{
    /// <summary>
    /// True when a connection can be opened. Never throws: an unreachable database is the
    /// answer this question exists to give, not a failure to answer it.
    /// </summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
