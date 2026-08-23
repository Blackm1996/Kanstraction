namespace Kanstraction.OnSite.Application.Common;

public sealed record ApplicationError(string Code, string Message);

public class Result
{
    protected Result(bool isSuccess, ApplicationError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public ApplicationError? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(string code, string message)
        => new(false, new ApplicationError(code, message));
}

public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, ApplicationError? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static new Result<T> Failure(string code, string message)
        => new(false, default, new ApplicationError(code, message));
}
