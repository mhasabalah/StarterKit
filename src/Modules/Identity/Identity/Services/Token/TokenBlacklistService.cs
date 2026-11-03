using Microsoft.Extensions.Caching.Distributed;

namespace Identity.Services.Token;

public class TokenBlacklistService(
    IDistributedCache cache,
    ILogger<TokenBlacklistService> logger) : ITokenBlacklistService
{
    private const string BlacklistKeyPrefix = "token:blacklist:";
    private const string UserStampKeyPrefix = "user:stamp:";

    public async Task BlacklistTokenAsync(string token, DateTime expiresAt)
    {
        if (expiresAt <= DateTime.UtcNow)
        {
            return;
        }

        await cache.SetStringAsync(
            BlacklistKeyPrefix + TokenHasher.Hash(token),
            "revoked",
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt });
    }

    public async Task<bool> IsTokenBlacklistedAsync(string token)
    {
        try
        {
            return await cache.GetStringAsync(BlacklistKeyPrefix + TokenHasher.Hash(token)) is not null;
        }
        catch (Exception ex)
        {
            // Fail-open: availability over revocation when Redis is down. Signature/lifetime are still validated.
            logger.LogError(ex, "Failed to check token blacklist - Redis may be unavailable");
            return false;
        }
    }

    public async Task BlacklistUserAsync(Guid userId, string currentSecurityStampHash, TimeSpan duration)
    {
        await cache.SetStringAsync(
            UserStampKeyPrefix + userId,
            currentSecurityStampHash,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration });

        logger.LogInformation("Access tokens of user {UserId} revoked for {Duration}", userId, duration);
    }

    public async Task<bool> IsUserTokenRevokedAsync(Guid userId, string? tokenSecurityStampHash)
    {
        try
        {
            var currentStampHash = await cache.GetStringAsync(UserStampKeyPrefix + userId);
            return currentStampHash is not null &&
                   !string.Equals(currentStampHash, tokenSecurityStampHash, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check user revocation for {UserId} - Redis may be unavailable", userId);
            return false;
        }
    }
}
