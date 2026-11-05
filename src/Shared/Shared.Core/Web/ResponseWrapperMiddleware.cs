using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shared.Core.Response;

namespace Shared.Core.Web;

/// <summary>
/// Wraps successful JSON responses under <c>/api</c> in the <see cref="ApiResponse{TData}"/> envelope.
/// <list type="bullet">
/// <item>Bodiless statuses (204, 205, 304) and HEAD requests are passed through untouched (no body is ever written).</item>
/// <item>Non-JSON content (files, text, CSV, streams) is passed through untouched.</item>
/// <item>Responses that already are an envelope are detected via the <see cref="AlreadyWrappedItemKey"/>
/// HttpContext.Items flag (set by <see cref="ApiResponseMarkerFilter"/> when an endpoint returns an
/// <see cref="IApiResponse"/>), with a structural JSON check as a fallback for non-filtered endpoints.</item>
/// <item>Non-2xx responses are passed through (the exception handler / status-code pages produce the error envelope).</item>
/// </list>
/// </summary>
public class ResponseWrapperMiddleware
{
    /// <summary>Set <c>HttpContext.Items[AlreadyWrappedItemKey] = true</c> to tell the wrapper the body is already an envelope.</summary>
    public const string AlreadyWrappedItemKey = "__ApiResponseWrapped";

    private readonly RequestDelegate _next;

    public ResponseWrapperMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip wrapping for Swagger, OpenAPI, health checks, and other system endpoints
        if (!ShouldWrap(context.Request))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }

        var status = context.Response.StatusCode;
        if (status is < 200 or >= 300 || IsBodiless(status) || IsMarkedWrapped(context) || !IsWrappable(context, responseBody))
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream, context.RequestAborted);
            return;
        }

        object? data = null;
        if (responseBody.Length > 0)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            try
            {
                using var document = await JsonDocument.ParseAsync(responseBody, cancellationToken: context.RequestAborted);
                if (IsEnvelope(document.RootElement))
                {
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalBodyStream, context.RequestAborted);
                    return;
                }

                data = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Declared JSON but isn't: pass through rather than corrupt it.
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream, context.RequestAborted);
                return;
            }
        }

        var apiResponse = new ApiResponse<object>
        {
            IsSuccess = true,
            StatusCode = status,
            Data = data,
            SuccessMessage = GetSuccessMessage(status),
            Errors = new List<string>()
        };

        context.Response.ContentLength = null;
        await context.Response.WriteAsJsonAsync(apiResponse, context.RequestAborted);
    }

    private static bool ShouldWrap(HttpRequest request) =>
        !HttpMethods.IsHead(request.Method) &&
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    private static bool IsBodiless(int status) =>
        status is StatusCodes.Status204NoContent or StatusCodes.Status205ResetContent or StatusCodes.Status304NotModified;

    private static bool IsMarkedWrapped(HttpContext context) =>
        context.Items.TryGetValue(AlreadyWrappedItemKey, out var flag) && flag is true;

    /// <summary>Empty bodies (e.g. <c>Results.Ok()</c>) and JSON bodies are wrapped; anything else is not.</summary>
    private static bool IsWrappable(HttpContext context, MemoryStream body)
    {
        if (body.Length == 0)
            return true;

        var contentType = context.Response.ContentType;
        if (string.IsNullOrEmpty(contentType))
            return false;

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               (mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) &&
                mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Equals("application/problem+json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Fallback for endpoints the marker filter can't see: root object with isSuccess + statusCode + errors.</summary>
    private static bool IsEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        bool hasSuccess = false, hasStatus = false, hasErrors = false;
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("isSuccess") || property.Name.Equals("IsSuccess", StringComparison.Ordinal))
                hasSuccess = property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False;
            else if (property.NameEquals("statusCode") || property.Name.Equals("StatusCode", StringComparison.Ordinal))
                hasStatus = property.Value.ValueKind == JsonValueKind.Number;
            else if (property.NameEquals("errors") || property.Name.Equals("Errors", StringComparison.Ordinal))
                hasErrors = property.Value.ValueKind == JsonValueKind.Array;
        }

        return hasSuccess && hasStatus && hasErrors;
    }

    private static string? GetSuccessMessage(int statusCode)
    {
        return statusCode switch
        {
            201 => "Created successfully",
            200 => "Operation completed successfully",
            _ => null
        };
    }
}

/// <summary>
/// Endpoint filter that flags responses which already are an <see cref="IApiResponse"/> envelope
/// (e.g. <c>Results.Ok(ApiResponseExtensions.Success(x))</c>) so <see cref="ResponseWrapperMiddleware"/> leaves them alone.
/// Applied to every Carter endpoint via <c>app.WithApiResponseMarker().MapCarter()</c>.
/// </summary>
public sealed class ApiResponseMarkerFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        var value = result is IValueHttpResult valueResult ? valueResult.Value : result;
        if (value is IApiResponse)
            context.HttpContext.Items[ResponseWrapperMiddleware.AlreadyWrappedItemKey] = true;

        return result;
    }
}

public static class ResponseWrapperExtensions
{
    public static IApplicationBuilder UseResponseWrapper(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResponseWrapperMiddleware>();
    }

    /// <summary>Adds <see cref="ApiResponseMarkerFilter"/> to every endpoint mapped through the returned group.</summary>
    public static RouteGroupBuilder WithApiResponseMarker(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty);
        group.AddEndpointFilter<ApiResponseMarkerFilter>();
        return group;
    }
}
