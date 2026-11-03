using Identity.Contracts.Auth.Commands;
using Identity.Data.Repository.RefreshTokens;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.Logout;

public class LogoutHandler(
    ITokenBlacklistService tokenBlacklistService,
    IRefreshTokenRepository refreshTokens,
    ISessionRevoker sessionRevoker,
    IHttpContextService httpContextService,
    ILogger<LogoutHandler> logger) : ICommandHandler<LogoutCommand, LogoutResult>
{
    public async Task<LogoutResult> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid()
                     ?? throw new UnauthorizedException("Not authenticated");

        var accessToken = ExtractBearerToken();
        if (accessToken is not null)
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(accessToken))
            {
                await tokenBlacklistService.BlacklistTokenAsync(accessToken, handler.ReadJwtToken(accessToken).ValidTo);
            }
        }

        if (command.LogoutAllDevices)
        {
            await sessionRevoker.RevokeAllAsync(userId, "User logged out from all devices", cancellationToken);
            logger.LogInformation("User {UserId} logged out from all devices", userId);
            return new LogoutResult(true, "Logged out from all devices successfully");
        }

        if (!string.IsNullOrEmpty(command.RefreshToken))
        {
            await refreshTokens.RevokeAsync(
                userId,
                TokenHasher.Hash(command.RefreshToken),
                "User logout",
                httpContextService.GetIpAddress(),
                cancellationToken);
        }

        logger.LogInformation("User {UserId} logged out", userId);
        return new LogoutResult(true, "Logged out successfully");
    }

    private string? ExtractBearerToken()
    {
        var header = httpContextService.GetHeaderValue("Authorization");
        return header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
