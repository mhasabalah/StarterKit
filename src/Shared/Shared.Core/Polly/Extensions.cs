using Polly;
using Shared.Core.Exceptions;
using Exception = System.Exception;

namespace Shared.Core.Polly;

public static class Extensions
{
    public static ILogger Logger { get; set; } = null!;

    public static T RetryOnFailure<T>(this object retrySource, Func<T> action, int retryCount = 3)
    {
        var retryPolicy = Policy
            .Handle<Exception>(ex =>
                ex is not ConcurrencyConflictException
                && ex is not DbUpdateConcurrencyException
                && ex is not ValidationException
                && ex is not NotFoundException
                && ex is not BadRequestException
                && ex is not ForbiddenException
                && ex is not UnauthorizedException)
            .Retry(retryCount, (exception, retryAttempt, context) =>
            {
                Logger.LogInformation("Retry attempt: {RetryAttempt}", retryAttempt);
                Logger.LogError("Exception: {Message}", exception.Message);
            });

        return retryPolicy.Execute(action);
    }
}
