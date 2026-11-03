namespace Identity.Contracts.Auth.Commands;

/// <summary>
/// Verifies the signed-in user's phone with the code from <see cref="SendPhoneVerificationCommand"/>.
/// <see cref="UserId"/> is always the authenticated caller (set by the endpoint from claims, never from the body).
/// Does not issue tokens: refresh the session to get updated claims.
/// </summary>
public record VerifyPhoneCommand(
    Guid UserId,
    string VerificationCode
) : ICommand<VerifyPhoneResult>;

public record VerifyPhoneResult(
    string Message,
    bool IsProfileComplete
);
