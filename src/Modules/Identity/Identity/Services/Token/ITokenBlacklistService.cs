namespace Identity.Services.Token;

/// <summary>
/// Service for managing blacklisted/revoked JWT tokens
/// </summary>
public interface ITokenBlacklistService
{
    /// <summary>
    /// Add an access token to the blacklist (revoke it)
    /// </summary>
    /// <param name="token">JWT access token to blacklist</param>
    /// <param name="expiresAt">When the token expires (for cleanup)</param>
    Task BlacklistTokenAsync(string token, DateTime expiresAt);

    /// <summary>
    /// Add a refresh token to the blacklist (revoke it)
    /// </summary>
    /// <param name="refreshToken">Refresh token to blacklist</param>
    /// <param name="expiresAt">When the token expires (for cleanup)</param>
    Task BlacklistRefreshTokenAsync(string refreshToken, DateTime expiresAt);

    /// <summary>
    /// Check if an access token is blacklisted
    /// </summary>
    /// <param name="token">JWT access token to check</param>
    /// <returns>True if token is blacklisted, false otherwise</returns>
    Task<bool> IsTokenBlacklistedAsync(string token);

    /// <summary>
    /// Check if a refresh token is blacklisted
    /// </summary>
    /// <param name="refreshToken">Refresh token to check</param>
    /// <returns>True if token is blacklisted, false otherwise</returns>
    Task<bool> IsRefreshTokenBlacklistedAsync(string refreshToken);

    /// <summary>
    /// Remove expired tokens from blacklist (cleanup)
    /// </summary>
    Task CleanupExpiredTokensAsync();

    /// <summary>
    /// Blacklist all tokens for a user (e.g., on account lock)
    /// </summary>
    Task BlacklistUserAsync(Guid userId, TimeSpan duration);

    /// <summary>
    /// Check if a user is blacklisted at the user level
    /// </summary>
    Task<bool> IsUserBlacklistedAsync(Guid userId);

    /// <summary>
    /// Remove the user-level blacklist entry (e.g., on account unlock)
    /// </summary>
    Task RemoveUserBlacklistAsync(Guid userId);
}
