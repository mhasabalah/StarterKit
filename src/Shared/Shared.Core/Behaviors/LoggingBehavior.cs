namespace Shared.Core.Behaviors;

public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    /// <summary>Requests taking longer than this are logged as a performance warning.</summary>
    public static readonly TimeSpan SlowRequestThreshold = TimeSpan.FromSeconds(3);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var responseName = typeof(TResponse).Name;

        logger.LogInformation("[START] Handling {Request}", requestName);

        var startTime = Stopwatch.GetTimestamp();
        var response = await next();
        var elapsed = Stopwatch.GetElapsedTime(startTime);
        var elapsedMs = (long)elapsed.TotalMilliseconds;

        if (elapsed > SlowRequestThreshold)
            logger.LogWarning(
                "[PERFORMANCE] Long running request: {Request} took {ElapsedMilliseconds} ms",
                requestName, elapsedMs);

        logger.LogInformation("[END] Handled {Response} in {ElapsedMilliseconds} ms",
            responseName, elapsedMs);
        return response;
    }
}
