using System.Net;

namespace Shared.Core.Exceptions;

public class GoneException : CustomException
{
    public GoneException(string message, int? code = null) : base(message,
        HttpStatusCode.Gone, code)
    {
    }
}
