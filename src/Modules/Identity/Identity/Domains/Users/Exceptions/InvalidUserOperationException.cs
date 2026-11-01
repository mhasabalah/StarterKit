using Shared.Core.Exceptions;

namespace Identity.Domains.Users.Exceptions;

/// <summary>
/// Exception thrown when attempting to perform an invalid operation on a user
/// </summary>
public class InvalidUserOperationException : BadRequestException
{
    public InvalidUserOperationException(string message)
        : base(message)
    {
    }
}
