namespace MeetingRooms.Application.Results;

/// <summary>
/// The outcome of a use case. Expected failures are <em>returned</em> as one of these,
/// never thrown - an exception reaching the global handler means nothing classified the
/// failure, which is a bug rather than a control-flow branch.
/// </summary>
public record OperationResult
{
    /// <summary>Error codes, not messages. The API layer maps them to status codes.</summary>
    public IReadOnlyList<string> Errors { get; protected init; } = [];

    public bool Succeeded { get; protected init; }

    protected OperationResult()
    {
    }

    public static OperationResult Success() => new() { Succeeded = true };

    public static OperationResult Failure(params IReadOnlyList<string> errors) =>
        new() { Errors = RequireErrors(errors) };

    /// <summary>
    /// A failure with no error code cannot be mapped to anything but a 500, so it is
    /// rejected at construction rather than surfacing later as a mystery response.
    /// </summary>
    protected static IReadOnlyList<string> RequireErrors(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            throw new ArgumentException("A failure must carry at least one error code.", nameof(errors));
        }

        // Copy: the caller keeps no handle on the result's internals.
        return [.. errors];
    }
}

/// <summary>A use case outcome that carries a value on success.</summary>
public record OperationResult<TValue> : OperationResult
    where TValue : notnull
{
    private readonly TValue _value = default!;

    /// <summary>
    /// Non-null whenever <see cref="OperationResult.Succeeded"/> is true. Reading it on a
    /// failed result throws rather than handing back a default, so a missed
    /// <see cref="OperationResult.Succeeded"/> check fails at the mistake, not downstream of it.
    /// </summary>
    public TValue Value => Succeeded
        ? _value
        : throw new InvalidOperationException(
            $"{nameof(Value)} is unavailable on a failed result. Check {nameof(Succeeded)} first.");

    private OperationResult()
    {
    }

    private OperationResult(TValue value)
    {
        _value = value;
        Succeeded = true;
    }

    /// <summary>Throws on a null value: a successful result must have something in it.</summary>
    public static OperationResult<TValue> Success(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new OperationResult<TValue>(value);
    }

    public new static OperationResult<TValue> Failure(params IReadOnlyList<string> errors) =>
        new() { Errors = RequireErrors(errors) };
}
