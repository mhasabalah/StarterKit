namespace Identity.Contracts.Admins.Commands;

/// <summary>
/// Command to promote a user to admin
/// </summary>
public record PromoteToAdminCommand : ICommand<PromoteToAdminResult>
{
    public Guid UserId { get; init; }
    public bool MakeSuperAdmin { get; init; }
    public string Reason { get; init; } = default!;
    public Guid PromotedByAdminId { get; init; }
}

public record PromoteToAdminResult(
    bool Success,
    string Message,
    IReadOnlyList<string> NewRoles
);
