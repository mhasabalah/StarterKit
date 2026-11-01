namespace Identity.Contracts.Admins.Commands;

/// <summary>
/// Command to assign a role to a user
/// </summary>
public record AssignRoleCommand : ICommand<AssignRoleResult>
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = default!;
    public string? Reason { get; init; }
    public Guid AssignedByAdminId { get; init; }
}

public record AssignRoleResult(
    bool Success,
    string Message,
    string RoleName
);
