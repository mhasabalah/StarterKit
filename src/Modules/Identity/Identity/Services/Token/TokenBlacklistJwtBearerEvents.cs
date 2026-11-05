using JsonWebToken = Microsoft.IdentityModel.JsonWebTokens.JsonWebToken;
using Shared.Core.Web;

namespace Identity.Services.Token;

/// <summary>
/// Rejects access tokens that were logged out (per-token blacklist) or issued before the user's sessions
/// were revoked (security-stamp revocation: lock, delete, password reset, role revoke, …).
/// <para>
/// Real-time hubs (<see cref="RealtimePaths.HubPrefix"/>): browsers cannot set <c>Authorization</c> on a WebSocket, so the
/// token is read from <c>?access_token=</c> there - and ONLY there. Every check below runs on the token actually
/// validated (<see cref="TokenValidatedContext.SecurityToken"/>), whichever way it arrived.
/// </para>
/// </summary>
public class TokenBlacklistJwtBearerEvents(
    ITokenBlacklistService tokenBlacklistService,
    ILogger<TokenBlacklistJwtBearerEvents> logger) : JwtBearerEvents
{
    public override Task MessageReceived(MessageReceivedContext context)
    {
        var request = context.Request;
        if (string.IsNullOrEmpty(context.Token)
            && RealtimePaths.IsHubRequest(request.Path)
            && string.IsNullOrEmpty(request.Headers.Authorization.ToString())
            && request.Query.TryGetValue(RealtimePaths.AccessTokenQueryParameter, out var token)
            && !string.IsNullOrWhiteSpace(token.ToString()))
        {
            context.Token = token.ToString();
        }

        return base.MessageReceived(context);
    }

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var rawToken = RawToken(context);
        if (rawToken is not null && await tokenBlacklistService.IsTokenBlacklistedAsync(rawToken))
        {
            logger.LogWarning("Attempt to use a revoked access token");
            context.Fail("Token has been revoked");
            return;
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

    /// <summary>
    /// The compact token that was validated. Taken from the validated security token (header OR hub query string), so
    /// no transport can skip the blacklist; the header is the fallback for handlers that do not expose it.
    /// </summary>
    public static string? RawToken(TokenValidatedContext context)
    {
        switch (context.SecurityToken)
        {
            case JsonWebToken jwt when !string.IsNullOrEmpty(jwt.EncodedToken):
                return jwt.EncodedToken;
            case JwtSecurityToken legacy when !string.IsNullOrEmpty(legacy.RawData):
                return legacy.RawData;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }
}
