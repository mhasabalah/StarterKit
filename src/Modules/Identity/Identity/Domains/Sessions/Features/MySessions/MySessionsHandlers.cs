using Identity.Contracts.Sessions;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Sessions.Exceptions;
using Identity.Services.Sessions;
using Shared.Core.Web;

namespace Identity.Domains.Sessions.Features.MySessions;

public class GetMySessionsHandler(
    IRefreshTokenRepository refreshTokens,
    ISessionManager sessions,
    IHttpContextService httpContextService,
    TimeProvider timeProvider) : IQueryHandler<GetMySessionsQuery, List<SessionDto>>
{
    public async Task<List<SessionDto>> Handle(GetMySessionsQuery query, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");
        var current = sessions.GetCurrentSessionId();

        var active = await refreshTokens.ListActiveFamiliesAsync(
            userId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return active.Select(s => SessionMapping.ToDto(s, current)).ToList();
    }
}

public class RevokeMySessionCommandValidator : AbstractValidator<RevokeMySessionCommand>
{
    public RevokeMySessionCommandValidator() => RuleFor(x => x.SessionId).NotEmpty();
}

public class RevokeMySessionHandler(
    ISessionManager sessions,
    IHttpContextService httpContextService) : ICommandHandler<RevokeMySessionCommand, RevokeSessionsResult>
{
    public async Task<RevokeSessionsResult> Handle(RevokeMySessionCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");

        // Scoped to the caller: another user's session id looks exactly like an unknown one.
        if (!await sessions.RevokeAsync(userId, command.SessionId, "Session revoked by user", cancellationToken))
        {
            throw new SessionNotFoundException(command.SessionId);
        }

        return new RevokeSessionsResult(1);
    }
}

public class RevokeOtherSessionsHandler(
    ISessionManager sessions,
    IHttpContextService httpContextService) : ICommandHandler<RevokeOtherSessionsCommand, RevokeSessionsResult>
{
    public async Task<RevokeSessionsResult> Handle(RevokeOtherSessionsCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");

        var count = await sessions.RevokeOthersAsync(
            userId, sessions.GetCurrentSessionId(), "Other sessions revoked by user", cancellationToken);

        return new RevokeSessionsResult(count);
    }
}

internal static class SessionMapping
{
    public static SessionDto ToDto(ActiveSession s, Guid? currentSessionId) => new(
        s.FamilyId, s.DeviceName, s.UserAgent, s.IpAddress, s.SignedInAt, s.LastUsedAt, s.ExpiresAt,
        IsCurrent: currentSessionId == s.FamilyId, TenantId: s.TenantId);
}
