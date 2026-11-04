using System.Text.Json.Serialization;

namespace Shared.Core.Response;

/// <summary>Marker for the response envelope so the response wrapper can recognise already-wrapped results.</summary>
public interface IApiResponse;

public class ApiResponse<TData> : IApiResponse
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public TData? Data { get; set; }
    public string? SuccessMessage { get; set; }
    public List<string> Errors { get; set; } = new();

    /// <summary>Error responses only: application error code (<c>CustomException.Code</c>). Omitted when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Code { get; set; }

    /// <summary>Error responses only: stable string error code (<c>CustomException.ErrorCode</c>). Omitted when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    /// <summary>Error responses only: trace id to quote to support / find the server log entry. Omitted when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; }

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


