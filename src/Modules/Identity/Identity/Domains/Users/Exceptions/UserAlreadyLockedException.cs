using Shared.Core.Exceptions;

namespace Identity.Domains.Users.Exceptions;

/// <summary>
/// Exception thrown when attempting to lock an already locked user
/// </summary>
public class UserAlreadyLockedException : BadRequestException
{
    public UserAlreadyLockedException(Guid userId)
        : base($"User with ID '{userId}' is already locked.")
    {
    }
}
