using MeetingRooms.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.Persistence;

/// <inheritdoc cref="IDatabaseHealthProbe"/>
public sealed class DatabaseHealthProbe : IDatabaseHealthProbe
{
    /// <summary>
    /// Short on purpose. The context is configured with <c>EnableRetryOnFailure</c>, so an
    /// unreachable database would otherwise be retried with backoff for tens of seconds - and a
    /// health endpoint that hangs when the database is down reports nothing useful to the thing
    /// polling it.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly AppDbContext _dbContext;

    public DatabaseHealthProbe(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(ProbeTimeout);

        try
        {
            return await _dbContext.Database.CanConnectAsync(probeCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The probe ran out of time rather than the request being abandoned: for this
            // question, a database that cannot answer in three seconds is not reachable.
            return false;
        }
    }
}
