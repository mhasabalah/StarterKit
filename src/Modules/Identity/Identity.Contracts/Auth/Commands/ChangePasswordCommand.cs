namespace Identity.Contracts.Auth.Commands;

/// <summary>
/// Self-service password change for the signed-in user. Ends every other session and returns a fresh one.
/// <see cref="UserId"/> is always the authenticated caller (set by the endpoint from claims).
/// </summary>
public record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword
) : ICommand<ChangePasswordResult>;

public record ChangePasswordResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string Message
);
