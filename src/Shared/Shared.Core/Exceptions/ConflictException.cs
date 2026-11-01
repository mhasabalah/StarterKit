using System.Net;

namespace Shared.Core.Exceptions;

public class ConflictException : CustomException
{
    public ConflictException(string message, int? code = null) : base(message,
        HttpStatusCode.Conflict, code)
    {
    }
}
