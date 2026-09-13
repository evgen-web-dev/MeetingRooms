using MeetingRooms.Application.Results;

namespace MeetingRooms.Application.Interfaces;

/// <summary>
/// Makes a use case's writes atomic. One method, on purpose: it is the only shape that is
/// correct under <c>EnableRetryOnFailure</c>, which Azure SQL requires.
/// <para>
/// A begin/commit/rollback trio - the obvious shape, and the one the reference project uses -
/// throws <see cref="InvalidOperationException"/> the moment retries are enabled, because a
/// retrying execution strategy refuses user-initiated transactions it cannot replay. Folding
/// the strategy, the transaction and the save into one call means no caller can get that
/// wrong, and no caller has to know about it.
/// </para>
/// <para>
/// <paramref name="operation"/> may run more than once: a transient fault rolls the
/// transaction back and re-executes the whole delegate, so it must not depend on work done by
/// an earlier attempt.
/// </para>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction. Commits only when the returned
    /// result succeeded; a failed result rolls back, exactly as a thrown exception does.
    /// <para>
    /// That constraint is why <typeparamref name="TResult"/> is an
    /// <see cref="OperationResult"/> rather than anything at all. Expected failures are
    /// returned rather than thrown here, so a unit of work that committed whatever the
    /// delegate returned would persist the half-finished writes of a use case that had
    /// already decided it had failed.
    /// </para>
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TResult : OperationResult;
}
