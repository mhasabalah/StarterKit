namespace Identity.Contracts.Auth.Commands;

/// <summary>
/// Command to refresh an access token using a refresh token
/// Implements token rotation for security
/// </summary>
public record RefreshTokenCommand(
    string RefreshToken
) : ICommand<RefreshTokenResult>;

/// <summary>
/// Result of refresh token operation
/// Returns new access token and rotated refresh token
/// </summary>
public record RefreshTokenResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string Message = "Token refreshed successfully"
);
