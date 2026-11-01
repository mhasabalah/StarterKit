namespace Identity.Contracts.Auth.Commands;

/// <summary>
/// Command to logout and revoke tokens
/// </summary>
public record LogoutCommand(
    string? RefreshToken = null,
    bool LogoutAllDevices = false
) : ICommand<LogoutResult>;

public record LogoutResult(
    bool Success,
    string Message
);
