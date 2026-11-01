using Shared.Core.Exceptions;

namespace Shared.Core.Behaviors;

public class ConcurrencyHandlingBehavior<TRequest, TResponse>(
    ILogger<ConcurrencyHandlingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(
                "Concurrency conflict on {RequestType}: {Message}",
                typeof(TRequest).Name, ex.Message);

            throw new ConcurrencyConflictException(ex);
        }
    }
}
