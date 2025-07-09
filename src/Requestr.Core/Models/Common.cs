namespace Requestr.Core.Models;

public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public T? Value { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<string> Errors { get; private set; } = new();

    private Result(bool isSuccess, T? value, string? errorMessage, List<string>? errors = null)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        Errors = errors ?? new List<string>();
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string errorMessage) => new(false, default, errorMessage);
    public static Result<T> Failure(List<string> errors) => new(false, default, null, errors);
    public static Result<T> Failure(string errorMessage, List<string> errors) => new(false, default, errorMessage, errors);
}

public class Result
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<string> Errors { get; private set; } = new();

    private Result(bool isSuccess, string? errorMessage, List<string>? errors = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Errors = errors ?? new List<string>();
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string errorMessage) => new(false, errorMessage);
    public static Result Failure(List<string> errors) => new(false, null, errors);
    public static Result Failure(string errorMessage, List<string> errors) => new(false, errorMessage, errors);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Failure(string error) => new() { IsValid = false, Errors = [error] };
    public static ValidationResult Failure(List<string> errors) => new() { IsValid = false, Errors = errors };
}
