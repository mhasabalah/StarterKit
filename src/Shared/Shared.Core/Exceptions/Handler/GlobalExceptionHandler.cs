using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Shared.Core.Response;

namespace Shared.Core.Exceptions.Handler;

/// <summary>
/// The only exception handler. Maps via <see cref="ExceptionStatusMapper"/> and writes the standard
/// <see cref="ApiResponse{TData}"/> error envelope (<c>isSuccess=false, statusCode, errors[], traceId, code?</c>).
/// 5xx: full exception (with stack trace) is logged with LogError; the client only gets a generic message + traceId.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Non-standard "client closed request" status used when the caller aborted.</summary>
    public const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Method} {Path} was cancelled by the client",
                context.Request.Method, context.Request.Path.Value);
            context.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var mapping = ExceptionStatusMapper.Map(exception);
        var traceId = GetTraceId(context);

        if (mapping.IsServerError)
        {
            logger.LogError(exception,
                "Unhandled {ExceptionType} for {Method} {Path} (status {StatusCode}, traceId {TraceId})",
                exception.GetType().Name, context.Request.Method, context.Request.Path.Value,
                mapping.StatusCode, traceId);
        }
        else
        {
            logger.LogWarning(
                "{ExceptionType} for {Method} {Path} → {StatusCode}: {Message} (traceId {TraceId})",
                exception.GetType().Name, context.Request.Method, context.Request.Path.Value,
                mapping.StatusCode, exception.Message, traceId);
        }

        if (context.Response.HasStarted)
            return false;

        await WriteAsync(context, mapping, traceId, cancellationToken);
        return true;
    }

    public static string GetTraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    internal static Task WriteAsync(HttpContext context, ExceptionMapping mapping, string traceId,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = mapping.StatusCode;

        var body = new ApiResponse<object>
        {
            IsSuccess = false,
            StatusCode = mapping.StatusCode,
            Data = null,
            SuccessMessage = null,
            Errors = mapping.Errors.ToList(),
            Code = mapping.Code,
            ErrorCode = mapping.ErrorCode,
            TraceId = traceId
        };

        return context.Response.WriteAsJsonAsync(body, cancellationToken);
    }
}
