namespace Shared.Core.Behaviors;

public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var responseName = typeof(TResponse).Name;

        logger.LogInformation("[START] Handling {Request}", requestName);

        var startTime = Stopwatch.GetTimestamp();
        var response = await next();
        var delta = Stopwatch.GetElapsedTime(startTime);

        if (delta.Seconds > 3)
            logger.LogWarning(
                "[PERFORMANCE] Long running request: {Request} took {ElapsedMilliseconds} ms",
                requestName, delta.Seconds);

        logger.LogInformation("[END] Handled {Response} in {ElapsedMilliseconds} ms",
            responseName, delta.Seconds);
        return response;
    }
}
