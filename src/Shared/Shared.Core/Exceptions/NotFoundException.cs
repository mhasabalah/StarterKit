using System.Globalization;
using System.Net;

namespace Shared.Core.Exceptions;

public class NotFoundException : CustomException
{
    public NotFoundException(string message, int? code = null) : base(message,
        HttpStatusCode.NotFound, code)
    {
    }

    public NotFoundException(string name, object key)
        : base($"Entity \"{name}\" ({key}) was not found.", HttpStatusCode.NotFound)
    {
    }
}
