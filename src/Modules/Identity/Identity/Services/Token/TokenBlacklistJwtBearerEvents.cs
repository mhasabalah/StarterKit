using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;

namespace Identity.Services.Token;

/// <summary>
/// Custom JWT Bearer events to check token blacklist
/// </summary>
public class TokenBlacklistJwtBearerEvents(
    ITokenBlacklistService tokenBlacklistService,
    ILogger<TokenBlacklistJwtBearerEvents> logger) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        try
        {
            // Get the token from the request
            var token = context.Request.Headers["Authorization"].ToString();

            if (string.IsNullOrEmpty(token) || !token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                // Token exists (JWT validation passed), just continue
                await base.TokenValidated(context);
                return;
            }

            var jwtToken = token["Bearer ".Length..].Trim();

            // Check if token is blacklisted
            var isBlacklisted = await tokenBlacklistService.IsTokenBlacklistedAsync(jwtToken);

            if (isBlacklisted)
            {
                logger.LogWarning("Attempt to use blacklisted token");
                context.Fail("Token has been revoked");
                return;
            }

            // Check user-level blacklist (e.g., account locked by admin)
            var userIdClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var userId))
            {
                var isUserBlacklisted = await tokenBlacklistService.IsUserBlacklistedAsync(userId);
                if (isUserBlacklisted)
                {
                    logger.LogWarning("Attempt to use token for blacklisted user {UserId}", userId);
                    context.Fail("Account has been locked");
                    return;
                }
            }

            await base.TokenValidated(context);
        }
        catch (Exception ex)
        {
            // Log error but allow token through (fail-open)
            // Redis might be down, but we don't want to block all users
            logger.LogError(ex, "Error checking token blacklist - allowing token (Redis may be unavailable)");
            await base.TokenValidated(context);
        }
    }
}
