namespace GitDiary.Client.Infrastructure;

public readonly struct Result<T>
{
    private Result(T? value, bool isSuccess, string? error)
    {
        Value = value;
        IsSuccess = isSuccess;
        Error = error;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result<T> Success(T value) => new(value, true, null);
    public static Result<T> Failure(string error) => new(default, false, error);

    public T GetValueOrThrow()
    {
        if (IsFailure)
            throw new InvalidOperationException(Error);
        return Value!;
    }
}
