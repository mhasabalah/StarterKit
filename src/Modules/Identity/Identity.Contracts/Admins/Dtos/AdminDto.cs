namespace Identity.Contracts.Admins.Dtos;

/// <summary>
/// Admin user data transfer object
/// </summary>
public record AdminDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? PhoneNumber { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string FullName { get; init; } = default!;
    public string? ProfilePictureUrl { get; init; }
    
    // Admin-specific
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public bool IsSuperAdmin { get; init; }
    public DateTime? LastAdminActionAt { get; init; }
    
    // Status
    public bool IsActive { get; init; }
    public bool IsLocked { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    
    // Audit
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
}
