using System.Net;

namespace Shared.Core.Exceptions;

public class ForbiddenException : CustomException
{
    public ForbiddenException(string message, int? code = null) : base(message,
        HttpStatusCode.Forbidden, code)
    {
    }
}
