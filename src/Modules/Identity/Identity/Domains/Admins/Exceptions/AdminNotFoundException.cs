using Shared.Core.Exceptions;

namespace Identity.Domains.Admins.Exceptions;

/// <summary>
/// Exception thrown when an admin is not found
/// </summary>
public class AdminNotFoundException : NotFoundException
{
    public AdminNotFoundException(Guid adminId)
        : base($"Admin with ID '{adminId}' was not found.")
    {
    }
}
