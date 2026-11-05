using Identity.Data.Repository.RefreshTokens;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.Extensions.Options;
using Shared.Core.Web;

namespace Identity.Services.Sessions;

/// <summary>
/// Ends individual sessions (devices). A session is a refresh-token family, its id the <c>sid</c> claim:
/// the family is revoked in the database (authoritative) and the <c>sid</c> is blacklisted in Redis for the
/// access-token lifetime, so the device's current access token stops working immediately, not at expiry.
/// </summary>
public interface ISessionManager
{
    /// <summary>The caller's own session id (<c>sid</c> claim); null for API-key, impersonation or legacy tokens.</summary>
    Guid? GetCurrentSessionId();

    /// <summary>Ends one session of <paramref name="userId"/>. Returns false when it is unknown, foreign or already ended.</summary>
    Task<bool> RevokeAsync(Guid userId, Guid sessionId, string reason, CancellationToken ct = default);

    /// <summary>Ends all sessions of the user except <paramref name="exceptSessionId"/>; returns how many.</summary>
    Task<int> RevokeOthersAsync(Guid userId, Guid? exceptSessionId, string reason, CancellationToken ct = default);
}

public sealed class SessionManager(
    IRefreshTokenRepository refreshTokens,
    ITokenBlacklistService tokenBlacklist,
    IHttpContextService httpContextService,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<SessionManager> logger) : ISessionManager
{
    public Guid? GetCurrentSessionId()
    {
        var claim = httpContextService.GetClaimValue(IdentityClaimTypes.SessionId)
                    ?? httpContextService.GetClaimValue(ClaimTypes.Sid);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, string reason, CancellationToken ct = default)
    {
        var revoked = await refreshTokens.RevokeUserFamilyAsync(
            userId, sessionId, reason, httpContextService.GetIpAddress(), ct);

        if (revoked == 0) return false;

        await BlacklistAsync(sessionId);
        logger.LogInformation("Session {SessionId} of user {UserId} revoked. Reason: {Reason}", sessionId, userId, reason);
        return true;
    }

    public async Task<int> RevokeOthersAsync(Guid userId, Guid? exceptSessionId, string reason, CancellationToken ct = default)
    {
        var ended = await refreshTokens.RevokeOtherFamiliesAsync(
            userId, exceptSessionId, reason, httpContextService.GetIpAddress(),
            timeProvider.GetUtcNow().UtcDateTime, ct);

        foreach (var sessionId in ended)
        {
            await BlacklistAsync(sessionId);
        }

        logger.LogInformation("Revoked {Count} other sessions of user {UserId}. Reason: {Reason}", ended.Count, userId, reason);
        return ended.Count;
    }

    private async Task BlacklistAsync(Guid sessionId)
    {
        try
        {
            // Outlive the longest-lived access token that may still carry this sid.
            await tokenBlacklist.BlacklistSessionAsync(
                sessionId, TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenExpirationMinutes + 1));
        }
        catch (Exception ex)
        {
            // The refresh token is already revoked (DB is authoritative); only the in-flight access token survives
            // until it expires. Do not fail an operation that already committed.
            logger.LogError(ex, "Failed to blacklist session {SessionId} in Redis", sessionId);
        }
    }
}
