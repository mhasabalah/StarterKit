using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Shared.Core.Exceptions;
using Shared.Core.Response;

namespace Shared.Core.ProblemDetails;

public static class Extensions
{
    public static WebApplication UseCustomProblemDetails(this WebApplication app)
    {
        app.UseStatusCodePages(statusCodeHandlerApp =>
        {
            statusCodeHandlerApp.Run(async context =>
            {
                context.Response.ContentType = "application/json";

                var reasonPhrase = ReasonPhrases.GetReasonPhrase(context.Response.StatusCode);
                
                var apiResponse = new ApiResponse<object>
                {
                    IsSuccess = false,
                    StatusCode = context.Response.StatusCode,
                    Data = null,
                    SuccessMessage = null,
                    Errors = new List<string> { reasonPhrase }
                };

                await context.Response.WriteAsJsonAsync(apiResponse);
            });
        });

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // Required: without this, ASP.NET Core rethrows exceptions when the error
            // handler sets a 404 response, interpreting it as "no handler matched."
            AllowStatusCode404Response = true,
            ExceptionHandler = async context =>
            {
                context.Response.ContentType = "application/json";

                var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
                var exceptionType = exceptionHandlerFeature?.Error;

                if (exceptionType is not null)
                {
                    var loggerFactory = context.RequestServices.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger("ExceptionHandler");
                    logger?.LogError(
                        "Error Message: {exceptionMessage}, Time of occurrence {time}",
                        exceptionType.Message, DateTime.UtcNow);

                    (string Detail, int StatusCode, List<string> Errors) details = exceptionType switch
                    {
                        ConcurrencyConflictException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status409Conflict,
                            new List<string> { exceptionType.Message }
                        ),
                        ConflictException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status409Conflict,
                            new List<string> { exceptionType.Message }
                        ),
                        ValidationException validationException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status400BadRequest,
                            validationException.Errors.Select(e => e.ErrorMessage).ToList()
                        ),
                        UnauthorizedException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized,
                            new List<string> { exceptionType.Message }
                        ),
                        ForbiddenException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status403Forbidden,
                            new List<string> { exceptionType.Message }
                        ),
                        BadRequestException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status400BadRequest,
                            new List<string> { exceptionType.Message }
                        ),
                        NotFoundException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status404NotFound,
                            new List<string> { exceptionType.Message }
                        ),
                        AppException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status400BadRequest,
                            new List<string> { exceptionType.Message }
                        ),
                        DbUpdateConcurrencyException =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status409Conflict,
                            new List<string> { exceptionType.Message }
                        ),
                        Microsoft.EntityFrameworkCore.DbUpdateException dbUpdateEx =>
                        (
                            dbUpdateEx.InnerException?.Message ?? dbUpdateEx.Message,
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError,
                            new List<string> { dbUpdateEx.InnerException?.Message ?? dbUpdateEx.Message }
                        ),
                        _ =>
                        (
                            exceptionType.Message,
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError,
                            new List<string> { exceptionType.Message }
                        )
                    };

                    var apiResponse = new ApiResponse<object>
                    {
                        IsSuccess = false,
                        StatusCode = details.StatusCode,
                        Data = null,
                        SuccessMessage = null,
                        Errors = details.Errors
                    };

                    await context.Response.WriteAsJsonAsync(apiResponse);
                }
            }
        });

        return app;
    }
}
