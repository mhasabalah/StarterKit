namespace Identity.Contracts.Users.Dtos;

/// <summary>
/// Simplified user DTO for list views
/// </summary>
public record UserListDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? PhoneNumber { get; init; }
    public string FullName { get; init; } = default!;
    public string? ProfilePictureUrl { get; init; }
    
    public bool IsActive { get; init; }
    public bool IsLocked { get; init; }
    public bool EmailConfirmed { get; init; }
    
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
}
