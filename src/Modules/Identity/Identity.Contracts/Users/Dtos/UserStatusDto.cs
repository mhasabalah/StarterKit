namespace Identity.Contracts.Users.Dtos;

/// <summary>
/// User account status information
/// </summary>
public record UserStatusDto
{
    public Guid UserId { get; init; }
    public bool IsActive { get; init; }
    public bool IsLocked { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public string? LockoutReason { get; init; }
    public DateTime? LastStatusChange { get; init; }
    public string? ChangedBy { get; init; }
}
