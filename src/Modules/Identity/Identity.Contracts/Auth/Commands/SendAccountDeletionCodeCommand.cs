namespace Identity.Contracts.Auth.Commands;

/// <summary>Sends the SMS code that re-authenticates the deletion of an account that has no password.</summary>
public sealed record SendAccountDeletionCodeCommand(Guid UserId) : ICommand<SendAccountDeletionCodeResult>;

public sealed record SendAccountDeletionCodeResult(string Message, DateTime ExpiresAt);
