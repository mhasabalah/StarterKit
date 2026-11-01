namespace Identity.Contracts.Auth.Commands;

public record SendPhoneVerificationCommand(
    Guid UserId
) : ICommand<SendPhoneVerificationResult>;

public record SendPhoneVerificationResult(
    string Message,
    DateTime ExpiresAt
);
