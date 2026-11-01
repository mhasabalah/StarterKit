using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text;

namespace Identity.Services.Token;

/// <summary>
/// In-memory token blacklist service with distributed cache support
/// For production: configure Redis distributed cache in DI
/// </summary>
public class TokenBlacklistService(
    IDistributedCache cache,
    ILogger<TokenBlacklistService> logger) : ITokenBlacklistService
{
    private const string BlacklistKeyPrefix = "token:blacklist:";
    private const string RefreshTokenBlacklistKeyPrefix = "refreshtoken:blacklist:";

    public async Task BlacklistTokenAsync(string token, DateTime expiresAt)
    {
        try
        {
            // Hash token for security (don't store raw tokens)
            var tokenHash = ComputeTokenHash(token);
            var cacheKey = $"{BlacklistKeyPrefix}{tokenHash}";

            // Calculate expiration time
            var expirationTime = expiresAt - DateTime.UtcNow;
            if (expirationTime <= TimeSpan.Zero)
            {
                // Token already expired, no need to blacklist
                logger.LogInformation("Token already expired, skipping blacklist");
                return;
            }

            // Store in cache with expiration
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = expiresAt
            };

            await cache.SetStringAsync(cacheKey, "revoked", options);

            logger.LogInformation("Token blacklisted until {ExpiresAt}", expiresAt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to blacklist token");
            throw;
        }
    }

    public async Task<bool> IsTokenBlacklistedAsync(string token)
    {
        try
        {
            var tokenHash = ComputeTokenHash(token);
            var cacheKey = $"{BlacklistKeyPrefix}{tokenHash}";

            var value = await cache.GetStringAsync(cacheKey);
            return value != null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check token blacklist status - Redis may be unavailable");
            // Fail-open: if we can't check blacklist (e.g., Redis down), allow the token
            // This prioritizes availability over perfect security
            // The token will still be validated for signature, expiration, etc.
            return false;
        }
    }

    public async Task BlacklistRefreshTokenAsync(string refreshToken, DateTime expiresAt)
    {
        try
        {
            // Hash token for security (don't store raw tokens)
            var tokenHash = ComputeTokenHash(refreshToken);
            var cacheKey = $"{RefreshTokenBlacklistKeyPrefix}{tokenHash}";

            // Calculate expiration time
            var expirationTime = expiresAt - DateTime.UtcNow;
            if (expirationTime <= TimeSpan.Zero)
            {
                // Token already expired, no need to blacklist
                logger.LogInformation("Refresh token already expired, skipping blacklist");
                return;
            }

            // Store in cache with expiration
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = expiresAt
            };

            await cache.SetStringAsync(cacheKey, "revoked", options);

            logger.LogInformation("Refresh token blacklisted until {ExpiresAt}", expiresAt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to blacklist refresh token");
            throw;
        }
    }

    public async Task<bool> IsRefreshTokenBlacklistedAsync(string refreshToken)
    {
        try
        {
            var tokenHash = ComputeTokenHash(refreshToken);
            var cacheKey = $"{RefreshTokenBlacklistKeyPrefix}{tokenHash}";

            var value = await cache.GetStringAsync(cacheKey);
            return value != null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check refresh token blacklist status - Redis may be unavailable");
            // Fail-open: if we can't check blacklist (e.g., Redis down), allow the token
            // This prioritizes availability over perfect security
            // The token will still be validated for expiration in database
            return false;
        }
    }

    public async Task BlacklistUserAsync(Guid userId, TimeSpan duration)
    {
        try
        {
            var cacheKey = $"user:blacklist:{userId}";
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration
            };
            await cache.SetStringAsync(cacheKey, "locked", options);
            logger.LogInformation("User {UserId} blacklisted for {Duration}", userId, duration);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to blacklist user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> IsUserBlacklistedAsync(Guid userId)
    {
        try
        {
            var cacheKey = $"user:blacklist:{userId}";
            var value = await cache.GetStringAsync(cacheKey);
            return value != null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check user blacklist status for {UserId} - Redis may be unavailable", userId);
            return false;
        }
    }

    public async Task RemoveUserBlacklistAsync(Guid userId)
    {
        try
        {
            var cacheKey = $"user:blacklist:{userId}";
            await cache.RemoveAsync(cacheKey);
            logger.LogInformation("User {UserId} removed from blacklist", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove user {UserId} from blacklist", userId);
            throw;
        }
    }

    public async Task CleanupExpiredTokensAsync()
    {
        // With distributed cache, expired entries are automatically removed
        // This method is here for future custom implementations
        logger.LogInformation("Expired tokens automatically cleaned up by cache");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Compute SHA256 hash of token for secure storage
    /// </summary>
    private static string ComputeTokenHash(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}
