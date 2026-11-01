using Microsoft.AspNetCore.Http;

namespace Shared.Core.Response;

public static class ApiResponseExtensions
{
    public static ApiResponse<T> Success<T>(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = data,
            SuccessMessage = message,
            Errors = new List<string>()
        };
    }

    public static IResult SuccessResult(object? data = null, string? message = null, int statusCode = 200)
    {
        var response = new ApiResponse<object>
        {
            IsSuccess = true,
            StatusCode = statusCode,
            Data = data,
            SuccessMessage = message,
            Errors = new List<string>()
        };

        return statusCode switch
        {
            201 => Results.Json(response, statusCode: statusCode),
            204 => Results.StatusCode(statusCode),
            _ => Results.Json(response, statusCode: statusCode)
        };
    }

    public static IResult Error(List<string> errors, int statusCode = 400)
    {
        var response = new ApiResponse<object>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Data = null,
            SuccessMessage = null,
            Errors = errors
        };

        return Results.Json(response, statusCode: statusCode);
    }

    public static IResult Error(string error, int statusCode = 400)
    {
        return Error(new List<string> { error }, statusCode);
    }

    public static IResult Created(object? data = null, string? message = "Created successfully")
    {
        return SuccessResult(data, message, 201);
    }

    public static IResult NotFound(string? message = "Resource not found")
    {
        return Error(message ?? "Resource not found", 404);
    }

    public static IResult BadRequest(string? message = "Bad request")
    {
        return Error(message ?? "Bad request", 400);
    }
}


