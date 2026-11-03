using Identity.Contracts.Audit;

namespace Identity.Contracts.Users.Commands;

/// <summary>
/// SuperAdmin-only: removes another user's 2FA (authenticator key + recovery codes), e.g. after a lost phone.
/// Ends every session of that user. Audited.
/// </summary>
public record ResetUserTwoFactorCommand : ICommand<ResetUserTwoFactorResult>, IAuditedCommand
{
    public Guid UserId { get; init; }
    public string Reason { get; init; } = default!;
    public Guid ResetByAdminId { get; init; }
}

public record ResetUserTwoFactorResult(bool Success, string Message);
