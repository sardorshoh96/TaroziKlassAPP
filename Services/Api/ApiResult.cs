namespace TaroziAPP.Services.Api;

public sealed class ApiResult<T>
{
    private ApiResult(T data, bool isSuccess, string? errorMessage)
    {
        Data = data;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public T Data { get; }
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    public static ApiResult<T> Success(T data) => new(data, true, null);
    public static ApiResult<T> Failure(string? message) => new(default!, false, message);
}

