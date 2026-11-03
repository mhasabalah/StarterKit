using Shared.Core.Exceptions;

namespace Shared.Core.Behaviors;

/// <summary>
/// The ONE place an optimistic-concurrency failure is translated: EF's <see cref="DbUpdateConcurrencyException"/>
/// (thrown unchanged by every DbContext) becomes the domain <see cref="ConcurrencyConflictException"/>, which
/// <c>GlobalExceptionHandler</c> maps to HTTP 409. Code outside MediatR (jobs) sees the raw EF exception and should
/// use <see cref="ConcurrencyExceptionExtensions.IsConcurrencyConflict"/> to catch either form.
/// </summary>
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
