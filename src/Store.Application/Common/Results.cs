namespace Store.Application.Common;

/// <summary>
/// A page of results plus the metadata needed to render a pager.
/// </summary>
/// <remarks>
/// Every list endpoint returns this shape. There is no code path in the application that returns
/// an unbounded collection — the legacy store's product endpoint returned all 4,207 products
/// (11.3 MB, 50 s) because its pagination was advisory. Here it is structural.
/// </remarks>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>
/// Base for paged queries. Page size is clamped in the setter rather than validated, so no caller
/// — including a hand-crafted request — can ask for an unbounded page.
/// </summary>
public abstract record PagedQuery
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 24;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>
/// Outcome of an operation that can fail for expected, non-exceptional reasons.
/// Used where failure is part of normal flow — an invalid coupon, insufficient stock — so those
/// paths do not pay the cost of throwing, and cannot be accidentally swallowed.
/// </summary>
public readonly record struct Result
{
    public bool Succeeded { get; private init; }
    public string? Error { get; private init; }
    public ErrorKind Kind { get; private init; }

    public static Result Success() => new() { Succeeded = true };

    public static Result Failure(string error, ErrorKind kind = ErrorKind.Validation) =>
        new() { Succeeded = false, Error = error, Kind = kind };

    public static Result NotFound(string error = "Not found") =>
        Failure(error, ErrorKind.NotFound);

    public static Result Forbidden(string error = "Not permitted") =>
        Failure(error, ErrorKind.Forbidden);

    public static Result Conflict(string error) =>
        Failure(error, ErrorKind.Conflict);
}

/// <inheritdoc cref="Result"/>
public readonly record struct Result<T>
{
    public bool Succeeded { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }
    public ErrorKind Kind { get; private init; }

    public static Result<T> Success(T value) => new() { Succeeded = true, Value = value };

    public static Result<T> Failure(string error, ErrorKind kind = ErrorKind.Validation) =>
        new() { Succeeded = false, Error = error, Kind = kind };

    public static Result<T> NotFound(string error = "Not found") =>
        Failure(error, ErrorKind.NotFound);

    public static Result<T> Forbidden(string error = "Not permitted") =>
        Failure(error, ErrorKind.Forbidden);

    public static Result<T> Conflict(string error) =>
        Failure(error, ErrorKind.Conflict);

    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// The value of a successful result, non-null.
    /// </summary>
    /// <remarks>
    /// <see cref="Value"/> is declared nullable because it is meaningless on a failure, which
    /// means every call site that has already checked <see cref="Succeeded"/> still trips a
    /// nullable warning. This expresses the guarantee once, in the place that can actually make
    /// it, instead of scattering <c>!</c> across the callers — and it throws loudly if someone
    /// reads it without checking, rather than silently handing back a default.
    /// </remarks>
    public T Required => Succeeded && Value is not null
        ? Value
        : throw new InvalidOperationException(
            $"Result<{typeof(T).Name}>.Required was read on a failed result: {Error ?? "no value"}");
}

/// <summary>Maps a failure onto an HTTP status code at the endpoint boundary.</summary>
public enum ErrorKind
{
    Validation = 0,
    NotFound = 1,
    Forbidden = 2,
    Conflict = 3,
    Unauthorized = 4
}
