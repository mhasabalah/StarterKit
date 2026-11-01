namespace Shared.Core.Response;

public class ApiResponse<TData>
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public TData? Data { get; set; }
    public string? SuccessMessage { get; set; }
    public List<string> Errors { get; set; } = new();

    public ApiResponse()
    {
    }

    public ApiResponse(bool isSuccess, int statusCode, TData? data = default, string? successMessage = null, List<string>? errors = null)
    {
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        Data = data;
        SuccessMessage = successMessage;
        Errors = errors ?? new List<string>();
    }
}

public class ApiResponse : ApiResponse<object>
{
    public ApiResponse() : base()
    {
    }

    public ApiResponse(bool isSuccess, int statusCode, object? data = default, string? successMessage = null, List<string>? errors = null) 
        : base(isSuccess, statusCode, data, successMessage, errors)
    {
    }

    public static ApiResponse<T> Success<T>(T data, string? message = null)
    {
        return new ApiResponse<T>(true, 200, data, message);
    }

    public static ApiResponse Success(string? message = null)
    {
        return new ApiResponse(true, 200, successMessage: message);
    }
}


