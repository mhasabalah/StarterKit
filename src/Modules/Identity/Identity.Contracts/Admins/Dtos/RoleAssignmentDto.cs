namespace Identity.Contracts.Admins.Dtos;

/// <summary>
/// Role assignment information
/// </summary>
public record RoleAssignmentDto
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = default!;
    public string RoleName { get; init; } = default!;
    public DateTime AssignedAt { get; init; }
    public Guid AssignedBy { get; init; }
    public string AssignedByName { get; init; } = default!;
}
