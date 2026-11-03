using Microsoft.AspNetCore.Http;

namespace Shared.Core.Exceptions.Handler;

/// <summary>Result of mapping an exception to an HTTP response.</summary>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="Errors">Client-safe messages (never internal details for 5xx).</param>
/// <param name="Code">Optional application error code (<see cref="CustomException.Code"/>).</param>
public sealed record ExceptionMapping(int StatusCode, IReadOnlyList<string> Errors, int? Code = null)
{
    public bool IsServerError => StatusCode >= 500;
}

/// <summary>
/// The single exception → HTTP status table.
/// <list type="bullet">
/// <item><see cref="ValidationException"/> → 400 with every validation message.</item>
/// <item><see cref="ConcurrencyConflictException"/> / <see cref="DbUpdateConcurrencyException"/> → 409.</item>
/// <item><see cref="AggregateNotFoundException"/> → 404.</item>
/// <item><see cref="BadHttpRequestException"/> (malformed body, missing parameter, body too large) → its own status.</item>
/// <item><see cref="CustomException"/> and subclasses → <see cref="CustomException.StatusCode"/>
/// (BadRequest 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, Gone 410, Domain/App 400 by default).</item>
/// <item>Anything else (incl. <see cref="DbUpdateException"/>, Npgsql errors) → 500.</item>
/// </list>
/// Every 5xx carries only <see cref="GenericServerError"/>: exception/inner-exception/DB messages never reach the client.
/// </summary>
public static class ExceptionStatusMapper
{
    public const string GenericServerError = "An unexpected error occurred. Please try again later.";

    public static ExceptionMapping Map(Exception exception)
    {
        var mapping = exception switch
        {
            ValidationException validation => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                ValidationMessages(validation)),

            ConcurrencyConflictException conflict => new ExceptionMapping(
                StatusCodes.Status409Conflict, [conflict.Message]),

            DbUpdateConcurrencyException => new ExceptionMapping(
                StatusCodes.Status409Conflict, [new ConcurrencyConflictException().Message]),

            AggregateNotFoundException notFound => new ExceptionMapping(
                StatusCodes.Status404NotFound, [notFound.Message]),

            BadHttpRequestException badRequest => new ExceptionMapping(
                badRequest.StatusCode, [badRequest.Message]),

            CustomException custom => new ExceptionMapping(
                NormalizeStatus((int)custom.StatusCode), [custom.Message], custom.Code),

            _ => new ExceptionMapping(StatusCodes.Status500InternalServerError, [GenericServerError])
        };

        return mapping.IsServerError
            ? mapping with { Errors = [GenericServerError] }
            : mapping;
    }

    private static int NormalizeStatus(int status) =>
        status is >= 400 and <= 599 ? status : StatusCodes.Status500InternalServerError;

    private static IReadOnlyList<string> ValidationMessages(ValidationException validation)
    {
        var messages = validation.Errors
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.ErrorMessage))
            .Select(e => e.ErrorMessage)
            .Distinct()
            .ToList();

        return messages.Count > 0 ? messages : [validation.Message];
    }
}
