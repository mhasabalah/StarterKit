namespace Identity.Contracts.Admins.Commands;

/// <summary>
/// Command to revoke a role from a user
/// </summary>
public record RevokeRoleCommand : ICommand<RevokeRoleResult>
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = default!;
    public string? Reason { get; init; }
    public Guid RevokedByAdminId { get; init; }
}

public record RevokeRoleResult(
    bool Success,
    string Message,
    string RoleName
);
