using Identity.Contracts.Auth.Commands;
using Identity.Data;
using Identity.Services.Token;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace Identity.Domains.Auth.Features.Logout;

public class LogoutHandler(
    ITokenBlacklistService tokenBlacklistService,
    ApplicationIdentityDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    ILogger<LogoutHandler> logger) : ICommandHandler<LogoutCommand, LogoutResult>
{
    public async Task<LogoutResult> Handle(LogoutCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get access token from Authorization header
            var accessToken = ExtractTokenFromHeader();

            if (string.IsNullOrEmpty(accessToken))
            {
                logger.LogWarning("Logout attempt without access token");
                throw new BadRequestException("No token provided");
            }

            // Parse token to get expiration and user ID
            var tokenHandler = new JwtSecurityTokenHandler();

            if (!tokenHandler.CanReadToken(accessToken))
            {
                logger.LogWarning("Invalid token format in logout request");
                throw new BadRequestException("Invalid token format");
            }

            var jwtToken = tokenHandler.ReadJwtToken(accessToken);
            var expiresAt = jwtToken.ValidTo;

            // Get user ID from claims
            var userIdClaim = jwtToken.Claims
                .FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                logger.LogWarning("Invalid user ID in token claims");
                throw new BadRequestException("Invalid token");
            }

            // Get IP address for audit trail
            var ipAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";

            // 1. Blacklist access token in Redis
            await tokenBlacklistService.BlacklistTokenAsync(accessToken, expiresAt);
            logger.LogInformation("Access token blacklisted for user {UserId}", userId);

            // 2. Revoke and blacklist refresh token if provided
            if (!string.IsNullOrEmpty(command.RefreshToken))
            {
                var refreshTokenHash = ComputeTokenHash(command.RefreshToken);
                var refreshToken = await dbContext.RefreshTokens
                    .FirstOrDefaultAsync(rt =>
                        rt.TokenHash == refreshTokenHash &&
                        rt.UserId == userId,
                        cancellationToken);

                if (refreshToken != null && refreshToken.IsActive)
                {
                    // Revoke in database
                    refreshToken.RevokedAt = DateTime.UtcNow;
                    refreshToken.RevokedReason = "User logout";
                    refreshToken.RevokedByIp = ipAddress;

                    await dbContext.SaveChangesAsync(cancellationToken);

                    // Blacklist in Redis (for fast lookup)
                    await tokenBlacklistService.BlacklistRefreshTokenAsync(
                        command.RefreshToken,
                        refreshToken.ExpiresAt);

                    logger.LogInformation(
                        "Refresh token revoked for user {UserId}",
                        userId);
                }
            }

            // 3. Optionally: Revoke ALL refresh tokens for this user (logout all devices)
            if (command.LogoutAllDevices)
            {
                var activeTokens = await dbContext.RefreshTokens
                    .Where(rt => rt.UserId == userId && !rt.RevokedAt.HasValue)
                    .ToListAsync(cancellationToken);

                foreach (var token in activeTokens)
                {
                    token.RevokedAt = DateTime.UtcNow;
                    token.RevokedReason = "User logged out from all devices";
                    token.RevokedByIp = ipAddress;
                }

                if (activeTokens.Any())
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation(
                        "All {Count} active refresh tokens revoked for user {UserId}",
                        activeTokens.Count,
                        userId);
                }
            }

            var message = command.LogoutAllDevices
                ? "Logged out from all devices successfully"
                : "Logged out successfully";

            logger.LogInformation(
                "User {UserId} logged out successfully from IP {IpAddress}",
                userId,
                ipAddress);

            return new LogoutResult(true, message);
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during logout");
            throw new BadRequestException("Logout failed");
        }
    }

    private string? ExtractTokenFromHeader()
    {
        var authHeader = httpContextAccessor.HttpContext?.Request.Headers["Authorization"]
            .ToString();

        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader["Bearer ".Length..].Trim();
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
