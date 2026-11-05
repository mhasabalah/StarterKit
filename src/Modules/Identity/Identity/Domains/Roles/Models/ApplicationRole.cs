using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Roles.Models;

/// <summary>
/// Application role extending ASP.NET Core Identity
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationRole() : base()
    {
    }

    public ApplicationRole(string roleName, string? description = null) : base(roleName)
    {
        Description = description;
        CreatedAt = DateTime.UtcNow;
    }

    // Factory method for seeding with specific ID
    public ApplicationRole(Guid id, string roleName, string? description = null) : base(roleName)
    {
        Id = id;
        Description = description;
        CreatedAt = DateTime.UtcNow;
    }
}
