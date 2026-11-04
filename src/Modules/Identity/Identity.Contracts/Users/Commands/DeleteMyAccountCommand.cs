using Identity.Contracts.Audit;

namespace Identity.Contracts.Users.Commands;

/// <summary>
/// Self-service account deletion / erasure (store account-deletion policy, GDPR art. 17). Re-authenticates the caller:
/// <see cref="Password"/> for accounts that have one, otherwise an SMS <see cref="OtpCode"/> requested with
/// <c>SendAccountDeletionCodeCommand</c>. Irreversible.
/// </summary>
public sealed record DeleteMyAccountCommand(Guid UserId, string? Password = null, string? OtpCode = null)
    : ICommand<DeleteMyAccountResult>, IAuditedCommand;

public sealed record DeleteMyAccountResult(string Message);
