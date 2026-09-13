using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Results;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWork"/>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _dbContext;

    public UnitOfWork(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TResult : OperationResult
    {
        ArgumentNullException.ThrowIfNull(operation);

        // The strategy owns the retry loop, so the transaction has to be opened inside it:
        // a retry replays this whole delegate, and replaying it around a transaction opened
        // outside would re-use one that has already been rolled back.
        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async ct =>
        {
            // Disposing an uncommitted transaction rolls it back, which covers every exit
            // path that is not the commit below - thrown or returned.
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            if (!result.Succeeded)
            {
                return result;
            }

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return result;
        }, cancellationToken);
    }
}
