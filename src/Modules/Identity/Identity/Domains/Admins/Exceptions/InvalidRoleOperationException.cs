using Shared.Core.Exceptions;

namespace Identity.Domains.Admins.Exceptions;

/// <summary>
/// Exception thrown when a role operation is invalid
/// </summary>
public class InvalidRoleOperationException : BadRequestException
{
    public InvalidRoleOperationException(string message)
        : base(message)
    {
    }
}
