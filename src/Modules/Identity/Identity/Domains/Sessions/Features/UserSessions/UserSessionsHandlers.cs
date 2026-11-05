using Identity.Contracts.Sessions;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Domains.Sessions.Exceptions;
using Identity.Domains.Sessions.Features.MySessions;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Resources;
using Identity.Services.Sessions;
using Microsoft.Extensions.Localization;
using Shared.Core.Web;

namespace Identity.Domains.Sessions.Features.UserSessions;

public class GetUserSessionsHandler(
    UserManager<ApplicationUser> userManager,
    IRefreshTokenRepository refreshTokens,
    TimeProvider timeProvider) : IQueryHandler<GetUserSessionsQuery, List<SessionDto>>
{
    public async Task<List<SessionDto>> Handle(GetUserSessionsQuery query, CancellationToken cancellationToken)
    {
        _ = await userManager.FindByIdAsync(query.UserId.ToString())
            ?? throw new UserNotFoundException(query.UserId);

        var active = await refreshTokens.ListActiveFamiliesAsync(
            query.UserId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return active.Select(s => SessionMapping.ToDto(s, currentSessionId: null)).ToList();
    }
}

public class RevokeUserSessionCommandValidator : AbstractValidator<RevokeUserSessionCommand>
{
    public RevokeUserSessionCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
    }
}

public class RevokeUserSessionHandler(
    UserManager<ApplicationUser> userManager,
    ISessionManager sessions,
    IIdentityAuditWriter audit,
    IHttpContextService httpContextService,
    IStringLocalizer<IdentityResource> localizer) : ICommandHandler<RevokeUserSessionCommand, RevokeSessionsResult>
{
    public async Task<RevokeSessionsResult> Handle(RevokeUserSessionCommand command, CancellationToken cancellationToken)
    {
        var callerId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        // Support may sign customers out, but only a SuperAdmin may touch another staff member's devices.
        var targetRoles = await userManager.GetRolesAsync(user);
        if (RoleHierarchy.IsStaff(targetRoles) && callerId != user.Id
            && !RoleHierarchy.IsSuperAdmin(httpContextService.GetRoles().ToList()))
        {
            throw new ForbiddenException(localizer["Session.StaffTargetForbidden"]);
        }

        if (!await sessions.RevokeAsync(user.Id, command.SessionId, "Session revoked by admin", cancellationToken))
        {
            throw new SessionNotFoundException(command.SessionId);
        }

        audit.Record(IdentityAuditActions.SessionRevoked, user.Id, reason: "Session revoked by admin",
            after: new { sessionId = command.SessionId }, actorId: callerId);

        return new RevokeSessionsResult(1);
    }
}
