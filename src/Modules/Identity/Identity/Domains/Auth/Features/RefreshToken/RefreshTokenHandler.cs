using Identity.Contracts.Auth.Commands;
using Identity.Data;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Identity.Domains.Auth.Features.RefreshToken;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .WithMessage("Refresh token is required");
    }
}

public class RefreshTokenHandler(
    ApplicationIdentityDbContext dbContext,
    IJwtTokenGenerator tokenGenerator,
    ITokenBlacklistService blacklistService,
    IHttpContextAccessor httpContextAccessor,
    IOptions<JwtOptions> jwtOptions,
    ILogger<RefreshTokenHandler> logger)
    : ICommandHandler<RefreshTokenCommand, RefreshTokenResult>
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<RefreshTokenResult> Handle(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Hash the refresh token
        var tokenHash = ComputeTokenHash(command.RefreshToken);

        // 2. Find refresh token in database
        var refreshToken = await dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (refreshToken == null)
        {
            logger.LogWarning("Refresh token not found in database");
            throw new BadRequestException("Invalid refresh token");
        }

        // 3. Check if blacklisted in Redis (fast check)
        var isBlacklisted = await blacklistService
            .IsRefreshTokenBlacklistedAsync(command.RefreshToken);

        if (isBlacklisted)
        {
            logger.LogWarning(
                "Attempted to use blacklisted refresh token for user {UserId}",
                refreshToken.UserId);
            throw new BadRequestException("Refresh token has been revoked");
        }

        // 4. Validate refresh token
        if (!refreshToken.IsActive)
        {
            var reason = refreshToken.IsExpired ? "expired" : "revoked";
            logger.LogWarning(
                "Inactive ({Reason}) refresh token used by user {UserId}",
                reason,
                refreshToken.UserId);
            throw new BadRequestException($"Refresh token is {reason}");
        }

        // 5. Get IP address for audit trail
        var ipAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        // 6. Revoke old refresh token (Token Rotation Pattern)
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        refreshToken.RevokedReason = "Replaced by new token (rotation)";

        // 7. Generate new access token
        var newAccessToken = await tokenGenerator.GenerateTokenAsync(refreshToken.User);

        // 8. Generate new refresh token
        var newRefreshTokenString = tokenGenerator.GenerateRefreshToken();
        var newRefreshTokenHash = ComputeTokenHash(newRefreshTokenString);

        // 9. Create new refresh token in database
        var newRefreshToken = new Models.RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = refreshToken.UserId,
            TokenHash = newRefreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        // 10. Save changes
        dbContext.RefreshTokens.Add(newRefreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Refresh token rotated for user {UserId} from IP {IpAddress}",
            refreshToken.UserId,
            ipAddress);

        // 12. Calculate access token expiration
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        return new RefreshTokenResult(
            newAccessToken,
            newRefreshTokenString,
            expiresAt,
            "Token refreshed successfully"
        );
    }

    /// <summary>
    /// Compute SHA256 hash of token for secure storage
    /// Never store raw tokens in database
    /// </summary>
    private static string ComputeTokenHash(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}
