namespace GitDiary.Client.Infrastructure;

public readonly struct Result<T>
{
    private Result(T? value, bool isSuccess, string? error, int? statusCode)
    {
        Value = value;
        IsSuccess = isSuccess;
        Error = error;
        StatusCode = statusCode;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    /// <summary>
    /// HTTP status code from the underlying request, when applicable.
    /// Set by <see cref="GitDiary.Client.Services.GitHubApiClient"/> so callers can
    /// classify errors (e.g. 409/422 → conflict) without pattern-matching English strings.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// True when the failure looks like a SHA / precondition conflict (409 or 422).
    /// </summary>
    public bool IsConflict => IsFailure && StatusCode is 409 or 422;

    public static Result<T> Success(T value) => new(value, true, null, null);

    public static Result<T> Failure(string error) => new(default, false, error, null);

    public static Result<T> Failure(string error, int? statusCode) =>
        new(default, false, error, statusCode);

    public T GetValueOrThrow()
    {
        if (IsFailure)
            throw new InvalidOperationException(Error);
        return Value!;
    }
}
