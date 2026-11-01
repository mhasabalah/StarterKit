using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Shared.Core.Exceptions.Handler;

public class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            "Error Message: {exceptionMessage}, Time of occurrence {time}",
            exception.Message, DateTime.UtcNow);

        (string Detail, string Title, int StatusCode) details = exception switch
        {
            // 5xx — never expose internal messages to the client; real message is already logged above
            InternalServerException =>
            (
                "An unexpected error occurred. Please try again later.",
                "InternalServerError",
                context.Response.StatusCode = StatusCodes.Status500InternalServerError
            ),
            // 4xx — user-facing messages are intentional and safe to return
            ValidationException =>
            (
                exception.Message,
                exception.GetType().Name,
                context.Response.StatusCode = StatusCodes.Status400BadRequest
            ),
            BadRequestException =>
            (
                exception.Message,
                exception.GetType().Name,
                context.Response.StatusCode = StatusCodes.Status400BadRequest
            ),
            UnauthorizedException =>
            (
                exception.Message,
                exception.GetType().Name,
                context.Response.StatusCode = StatusCodes.Status401Unauthorized
            ),
            NotFoundException =>
            (
                exception.Message,
                exception.GetType().Name,
                context.Response.StatusCode = StatusCodes.Status404NotFound
            ),
            ForbiddenException =>
            (
                exception.Message,
                exception.GetType().Name,
                context.Response.StatusCode = StatusCodes.Status403Forbidden
            ),
            ConflictException =>
            (
                exception.Message,
                exception.GetType().Name,
                context.Response.StatusCode = StatusCodes.Status409Conflict
            ),
            GoneException =>
            (
                exception.Message,
                exception.GetType().Name,
                context.Response.StatusCode = StatusCodes.Status410Gone
            ),
            ConcurrencyConflictException =>
            (
                exception.Message,
                "ConcurrencyConflict",
                context.Response.StatusCode = StatusCodes.Status409Conflict
            ),
            // Catch-all — unhandled exception; never expose internals
            _ =>
            (
                "An unexpected error occurred. Please try again later.",
                "InternalServerError",
                context.Response.StatusCode = StatusCodes.Status500InternalServerError
            )
        };

        Microsoft.AspNetCore.Mvc.ProblemDetails problemDetails = new()
        {
            Title = details.Title,
            Detail = details.Detail,
            Status = details.StatusCode,
            Instance = context.Request.Path
        };

        problemDetails.Extensions.Add("traceId", context.TraceIdentifier);

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions.Add("ValidationErrors", validationException.Errors);
        }

        await context.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
