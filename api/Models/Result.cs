namespace CloudArchive.Api.Models;

public readonly record struct Result<T>
{
    public bool    IsSuccess { get; }
    public T?      Value     { get; }
    public string? Error     { get; }

    private Result(bool isSuccess, T? value, string? error)
        => (IsSuccess, Value, Error) = (isSuccess, value, error);

    public static Result<T> Ok(T value)        => new(true,  value,   null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
