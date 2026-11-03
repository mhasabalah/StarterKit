using Identity.Contracts.Audit;

namespace Identity.Contracts.Admins.Commands;

/// <summary>
/// Command to create a new admin account
/// </summary>
public record CreateAdminCommand : ICommand<CreateAdminResult>, IAuditedCommand
{
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string Password { get; init; } = default!;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsSuperAdmin { get; init; }
    public Guid CreatedByAdminId { get; init; }
}

public record CreateAdminResult(
    bool Success,
    string Message,
    Guid? AdminId,
    IReadOnlyList<string> Roles
);
