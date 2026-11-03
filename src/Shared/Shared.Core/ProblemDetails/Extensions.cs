using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Shared.Core.Exceptions.Handler;
using Shared.Core.Response;

namespace Shared.Core.ProblemDetails;

public static class Extensions
{
    /// <summary>Registers <see cref="GlobalExceptionHandler"/> (the single exception → response mapper).</summary>
    public static IServiceCollection AddCustomProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    /// <summary>
    /// Status-code pages (bodiless 4xx/5xx such as 401/403/404 from auth/routing) and exception handling.
    /// Both produce the <see cref="ApiResponse{TData}"/> error envelope. Requires <see cref="AddCustomProblemDetails"/>.
    /// </summary>
    public static IApplicationBuilder UseCustomProblemDetails(this IApplicationBuilder app)
    {
        app.UseStatusCodePages(statusCodeHandlerApp =>
        {
            statusCodeHandlerApp.Run(async context =>
            {
                var statusCode = context.Response.StatusCode;
                var apiResponse = new ApiResponse<object>
                {
                    IsSuccess = false,
                    StatusCode = statusCode,
                    Data = null,
                    SuccessMessage = null,
                    Errors = [ReasonPhrases.GetReasonPhrase(statusCode)],
                    TraceId = GlobalExceptionHandler.GetTraceId(context)
                };

                await context.Response.WriteAsJsonAsync(apiResponse);
            });
        });

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // Required: without this, ASP.NET Core rethrows exceptions when the error
            // handler sets a 404 response, interpreting it as "no handler matched."
            AllowStatusCode404Response = true,

            // Only reached if no IExceptionHandler handled the exception (e.g. GlobalExceptionHandler not registered).
            ExceptionHandler = async context =>
            {
                var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var mapping = error is null
                    ? new ExceptionMapping(StatusCodes.Status500InternalServerError, [ExceptionStatusMapper.GenericServerError])
                    : ExceptionStatusMapper.Map(error);

                if (error is not null && mapping.IsServerError)
                {
                    context.RequestServices.GetService<ILoggerFactory>()?
                        .CreateLogger("ExceptionHandler")
                        .LogError(error, "Unhandled exception for {Method} {Path}",
                            context.Request.Method, context.Request.Path.Value);
                }

                await GlobalExceptionHandler.WriteAsync(context, mapping, GlobalExceptionHandler.GetTraceId(context),
                    context.RequestAborted);
            }
        });

        return app;
    }
}
