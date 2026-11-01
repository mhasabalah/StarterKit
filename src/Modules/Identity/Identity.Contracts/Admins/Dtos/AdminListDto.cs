namespace Identity.Contracts.Admins.Dtos;

/// <summary>
/// Admin list item DTO
/// </summary>
public record AdminListDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string FullName { get; init; } = default!;
    public string? ProfilePictureUrl { get; init; }
    
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public bool IsSuperAdmin { get; init; }
    
    public bool IsActive { get; init; }
    public bool IsLocked { get; init; }
    
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime? LastAdminActionAt { get; init; }
}
