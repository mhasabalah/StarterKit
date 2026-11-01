namespace Identity.Contracts.Auth.Commands;

public record VerifyPhoneCommand(
    Guid UserId,
    string VerificationCode
) : ICommand<VerifyPhoneResult>;

public record VerifyPhoneResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string Message
);
