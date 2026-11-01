namespace Identity.Contracts.Admins.Commands;

/// <summary>
/// Command to demote an admin to regular user
/// </summary>
public record DemoteAdminCommand : ICommand<DemoteAdminResult>
{
    public Guid AdminId { get; init; }
    public string Reason { get; init; } = default!;
    public Guid DemotedByAdminId { get; init; }
}

public record DemoteAdminResult(
    bool Success,
    string Message,
    IReadOnlyList<string> RemainingRoles
);
