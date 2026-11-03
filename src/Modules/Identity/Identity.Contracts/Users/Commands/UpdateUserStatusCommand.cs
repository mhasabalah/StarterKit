using Identity.Contracts.Audit;

namespace Identity.Contracts.Users.Commands;

/// <summary>
/// Command to update user account status (activate/deactivate)
/// </summary>
public record UpdateUserStatusCommand : ICommand<UpdateUserStatusResult>, IAuditedCommand
{
    public Guid UserId { get; init; }
    public bool IsActive { get; init; }
    public string? Reason { get; init; }
    public Guid UpdatedByAdminId { get; init; }
}

public record UpdateUserStatusResult(
    bool Success,
    string Message,
    bool NewStatus
);
