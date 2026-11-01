namespace Identity.Contracts.Users.Dtos;

/// <summary>
/// User data transfer object
/// </summary>
public record UserDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? PhoneNumber { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string FullName { get; init; } = default!;
    public string? ProfilePictureUrl { get; init; }
    
    // OAuth
    public string? OAuthProvider { get; init; }
    
    // Status
    public bool IsActive { get; init; }
    public bool IsLocked { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool PhoneNumberConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public int AccessFailedCount { get; init; }
    
    // Profile
    public bool IsProfileComplete { get; init; }
    public DateTime? ProfileCompletedAt { get; init; }
    
    // Roles
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    
    // Audit
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
}
