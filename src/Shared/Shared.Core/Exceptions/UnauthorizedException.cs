using System.Net;

namespace Shared.Core.Exceptions;

public class UnauthorizedException : CustomException
{
    public UnauthorizedException(string message, int? code = null) : base(message,
        HttpStatusCode.Unauthorized, code)
    {
    }
}
