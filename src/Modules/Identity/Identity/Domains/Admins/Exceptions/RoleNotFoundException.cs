using Shared.Core.Exceptions;

namespace Identity.Domains.Admins.Exceptions;

/// <summary>
/// Exception thrown when a role is not found
/// </summary>
public class RoleNotFoundException : NotFoundException
{
    public RoleNotFoundException(string roleName)
        : base($"Role '{roleName}' was not found.")
    {
    }
}
