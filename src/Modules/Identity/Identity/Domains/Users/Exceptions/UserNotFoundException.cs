using Shared.Core.Exceptions;

namespace Identity.Domains.Users.Exceptions;

/// <summary>
/// Exception thrown when a user is not found
/// </summary>
public class UserNotFoundException : NotFoundException
{
    public UserNotFoundException(Guid userId)
        : base($"User with ID '{userId}' was not found.")
    {
    }
    
    public UserNotFoundException(string identifier)
        : base($"User '{identifier}' was not found.")
    {
    }
}
