namespace Identity.Contracts.Auth.Commands;

/// <summary>
/// Sends a verification code to the signed-in user's phone.
/// <see cref="UserId"/> is always the authenticated caller (set by the endpoint from claims, never from the body).
/// </summary>
public record SendPhoneVerificationCommand(
    Guid UserId
) : ICommand<SendPhoneVerificationResult>;

public record SendPhoneVerificationResult(
    string Message,
    DateTime ExpiresAt
);
