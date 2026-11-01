using System.Net;

namespace Shared.Core.Exceptions;

public class AppException : CustomException
{
    public AppException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest,
        int? code = null) : base(message, statusCode, code)
    {
    }

    public AppException(string message, Exception innerException,
        HttpStatusCode statusCode = HttpStatusCode.BadRequest, int? code = null) : base(message,
        innerException, statusCode, code)
    {
    }
}
