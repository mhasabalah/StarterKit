using System.Net;

namespace Shared.Core.Exceptions;

public class BadRequestException : CustomException
{
    public BadRequestException(string message, int? code = null) : base(message,
        HttpStatusCode.BadRequest, code)
    {
    }
}
