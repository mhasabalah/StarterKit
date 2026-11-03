using Identity.Data.Repository.RefreshTokens;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.Extensions.Options;
using Shared.Core.Web;

namespace Identity.Services.Sessions;

/// <summary>
/// Ends every session of a user: rotates the security stamp, revokes all refresh tokens in the database and
/// revokes all access tokens carrying the old stamp in Redis.
/// Called on delete, deactivate, lock, password reset/change, role revoke/demote, admin contact change, logout-all.
/// </summary>
public interface ISessionRevoker
{
    Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct = default);
}

public sealed class SessionRevoker(
    UserManager<ApplicationUser> userManager,
    IRefreshTokenRepository refreshTokens,
    ITokenBlacklistService tokenBlacklist,
    IHttpContextService httpContextService,
    IOptions<JwtOptions> jwtOptions,
    ILogger<SessionRevoker> logger) : ISessionRevoker
{
    public async Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var ipAddress = httpContextService.GetIpAddress();
        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is not null)
        {
            // A new stamp invalidates refresh tokens (stamp check on refresh) and access tokens (Redis check).
            var stampResult = await userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to rotate security stamp: {string.Join(", ", stampResult.Errors.Select(e => e.Code))}");
            }
        }

        var revoked = await refreshTokens.RevokeAllForUserAsync(userId, reason, ipAddress, ct);

        if (user is not null)
        {
            try
            {
                // Outlive the longest-lived access token that may still carry the old stamp.
                var duration = TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenExpirationMinutes + 1);
                await tokenBlacklist.BlacklistUserAsync(
                    userId, TokenHasher.HashSecurityStamp(user.SecurityStamp), duration);
            }
            catch (Exception ex)
            {
                // Refresh tokens are already revoked (DB is authoritative); only already-issued access tokens
                // survive until they expire. Do not fail the admin operation that already committed.
                logger.LogError(ex, "Failed to revoke access tokens in Redis for user {UserId}", userId);
            }
        }

        logger.LogInformation(
            "Revoked all sessions of user {UserId} ({Count} refresh tokens). Reason: {Reason}",
            userId, revoked, reason);
    }
}
