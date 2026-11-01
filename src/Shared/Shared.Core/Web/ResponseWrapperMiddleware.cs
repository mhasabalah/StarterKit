using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Shared.Core.Response;

namespace Shared.Core.Web;

public class ResponseWrapperMiddleware
{
    private readonly RequestDelegate _next;

    public ResponseWrapperMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip wrapping for Swagger, OpenAPI, health checks, and other system endpoints
        if (!ShouldWrapping(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;

        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);

        // Read the response body
        var responseBodyText = await new StreamReader(context.Response.Body).ReadToEndAsync();
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        // Only wrap if it's a success status code and response is not already wrapped
        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            // Check if response is already wrapped by checking if it contains IsSuccess property
            if (responseBodyText.Contains("\"isSuccess\"", StringComparison.OrdinalIgnoreCase))
            {
                // Already wrapped, just copy it back
                context.Response.Body = originalBodyStream;
                await responseBody.CopyToAsync(originalBodyStream);
                return;
            }

            object? data = null;
            
            // Try to deserialize the response if it's JSON
            if (!string.IsNullOrEmpty(responseBodyText) && context.Response.ContentType?.Contains("application/json") == true)
            {
                try
                {
                    data = System.Text.Json.JsonSerializer.Deserialize<object>(responseBodyText);
                }
                catch
                {
                    // If deserialization fails, use raw text
                    data = responseBodyText;
                }
            }

            var apiResponse = new ApiResponse<object>
            {
                IsSuccess = true,
                StatusCode = context.Response.StatusCode,
                Data = data,
                SuccessMessage = GetSuccessMessage(context.Response.StatusCode),
                Errors = new List<string>()
            };

            context.Response.Body = originalBodyStream;
            await context.Response.WriteAsJsonAsync(apiResponse);
        }
        else
        {
            // For non-success responses, restore original body and let exception handlers deal with it
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
        }
    }

    private static bool ShouldWrapping(PathString path)
    {
        // Skip wrapping for Swagger, OpenAPI, health checks, and other system endpoints
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetSuccessMessage(int statusCode)
    {
        return statusCode switch
        {
            201 => "Created successfully",
            200 => "Operation completed successfully",
            204 => "Operation completed successfully",
            _ => null
        };
    }
}

public static class ResponseWrapperExtensions
{
    public static IApplicationBuilder UseResponseWrapper(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResponseWrapperMiddleware>();
    }
}

