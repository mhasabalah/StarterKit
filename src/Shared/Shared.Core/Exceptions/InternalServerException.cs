using System.Globalization;
using System.Net;

namespace Shared.Core.Exceptions;

public class InternalServerException : CustomException
{
    public InternalServerException() : base() { }

    public InternalServerException(string message, int? code) : base(message, code: code) { }

    public InternalServerException(string message, int? code = null, params object[] args)
        : base(string.Format(CultureInfo.CurrentCulture, message, args,
            HttpStatusCode.InternalServerError, code))
    {
    }
}
