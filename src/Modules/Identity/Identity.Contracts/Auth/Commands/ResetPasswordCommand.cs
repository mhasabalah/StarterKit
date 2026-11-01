namespace Identity.Contracts.Auth.Commands;

public record ResetPasswordCommand(
    Guid? UserId,
    string? Email,
    string Token,
    string NewPassword,
    string ConfirmPassword
) : ICommand<ResetPasswordResult>;

public record ResetPasswordResult(string Message);
