using Identity.Contracts.Audit;

namespace Identity.Contracts.Users.Commands;

/// <summary>
/// Command to unlock/reactivate a user account
/// </summary>
public record UnlockUserCommand : ICommand<UnlockUserResult>, IAuditedCommand
{
    public Guid UserId { get; init; }
    public string? UnlockReason { get; init; }
    public Guid UnlockedByAdminId { get; init; }
}

public record UnlockUserResult(
    bool Success,
    string Message
);
