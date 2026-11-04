using System.Net;

namespace Shared.Core.Exceptions;

public class CustomException : Exception
{
    public CustomException(
        string message,
        HttpStatusCode statusCode = HttpStatusCode.InternalServerError,
        int? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public CustomException(
        string message,
        Exception innerException,
        HttpStatusCode statusCode = HttpStatusCode.InternalServerError,
        int? code = null) : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public CustomException(
        HttpStatusCode statusCode = HttpStatusCode.InternalServerError,
        int? code = null) : base()
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }

    public int? Code { get; }

    /// <summary>Optional stable machine-readable code (e.g. <c>feature_not_in_plan</c>), surfaced as <c>errorCode</c>.</summary>
    public string? ErrorCode { get; init; }
}
