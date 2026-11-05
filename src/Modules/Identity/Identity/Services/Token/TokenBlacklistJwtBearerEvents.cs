namespace Identity.Services.Token;

/// <summary>
/// Rejects access tokens that were logged out (per-token blacklist) or issued before the user's sessions
/// were revoked (security-stamp revocation: lock, delete, password reset, role revoke, …).
/// </summary>
public class TokenBlacklistJwtBearerEvents(
    ITokenBlacklistService tokenBlacklistService,
    ILogger<TokenBlacklistJwtBearerEvents> logger) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var rawToken = authorization["Bearer ".Length..].Trim();
            if (await tokenBlacklistService.IsTokenBlacklistedAsync(rawToken))
            {
                logger.LogWarning("Attempt to use a revoked access token");
                context.Fail("Token has been revoked");
                return;
            }
        }

        var principal = context.Principal;

        // Sign-out of ONE device: the session id (refresh-token family) is blacklisted for the access-token lifetime.
        var sessionClaim = principal?.FindFirst(IdentityClaimTypes.SessionId)?.Value
                           ?? principal?.FindFirst(ClaimTypes.Sid)?.Value;
        if (Guid.TryParse(sessionClaim, out var sessionId) && await tokenBlacklistService.IsSessionRevokedAsync(sessionId))
        {
            logger.LogWarning("Attempt to use an access token of a revoked session {SessionId}", sessionId);
            context.Fail("Session has been revoked");
            return;
        }

        var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            var stampHash = principal?.FindFirst(IdentityClaimTypes.SecurityStampHash)?.Value;
            if (await tokenBlacklistService.IsUserTokenRevokedAsync(userId, stampHash))
            {
                logger.LogWarning("Attempt to use a revoked session token for user {UserId}", userId);
                context.Fail("Session has been revoked");
                return;
            }
        }

        await base.TokenValidated(context);
    }
}
