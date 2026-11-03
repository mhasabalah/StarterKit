using Identity.Contracts.Audit;

namespace Identity.Contracts.Users.Commands;

/// <summary>
/// Command to soft delete a user account
/// </summary>
public record DeleteUserCommand : ICommand<DeleteUserResult>, IAuditedCommand
{
    public Guid UserId { get; init; }
    public string Reason { get; init; } = default!;
    public Guid DeletedByAdminId { get; init; }
}

public record DeleteUserResult(
    bool Success,
    string Message
);
