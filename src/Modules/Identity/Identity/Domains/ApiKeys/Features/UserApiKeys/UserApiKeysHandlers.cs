using Identity.Contracts.ApiKeys;
using Identity.Data.Repository.ApiKeys;
using Identity.Domains.ApiKeys.Exceptions;
using Identity.Domains.ApiKeys.Features.MyApiKeys;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Resources;
using Microsoft.Extensions.Localization;
using Shared.Core.Web;

namespace Identity.Domains.ApiKeys.Features.UserApiKeys;

public class GetUserApiKeysHandler(
    ApplicationIdentityDbContext dbContext,
    TimeProvider timeProvider) : IQueryHandler<GetUserApiKeysQuery, List<ApiKeyDto>>
{
    public async Task<List<ApiKeyDto>> Handle(GetUserApiKeysQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(u => u.Id == query.UserId, cancellationToken))
            throw new UserNotFoundException(query.UserId);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == query.UserId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(k => ApiKeyMapping.ToDto(k, now, k.UserId)).ToList();
    }
}

public class RevokeUserApiKeyCommandValidator : AbstractValidator<RevokeUserApiKeyCommand>
{
    public RevokeUserApiKeyCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.KeyId).NotEmpty();
    }
}

public class RevokeUserApiKeyHandler(
    UserManager<ApplicationUser> userManager,
    IApiKeyRepository keys,
    IIdentityAuditWriter audit,
    IHttpContextService httpContextService,
    TimeProvider timeProvider,
    IStringLocalizer<IdentityResource> localizer) : ICommandHandler<RevokeUserApiKeyCommand, RevokeApiKeyResult>
{
    public async Task<RevokeApiKeyResult> Handle(RevokeUserApiKeyCommand command, CancellationToken cancellationToken)
    {
        var callerId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        // Same rule as sessions: staff accounts are only touched by a SuperAdmin.
        var targetRoles = await userManager.GetRolesAsync(user);
        if (RoleHierarchy.IsStaff(targetRoles) && callerId != user.Id
            && !RoleHierarchy.IsSuperAdmin(httpContextService.GetRoles().ToList()))
        {
            throw new ForbiddenException(localizer["Session.StaffTargetForbidden"]);
        }

        var key = await keys.FindOwnedAsync(user.Id, command.KeyId, cancellationToken)
                  ?? throw new ApiKeyNotFoundException(command.KeyId);

        var revoked = key.Revoke(timeProvider.GetUtcNow().UtcDateTime, callerId);
        if (revoked)
        {
            await keys.SaveAsync(cancellationToken);
            audit.Record(IdentityAuditActions.ApiKeyRevoked, user.Id, reason: "API key revoked by admin",
                after: new { keyId = key.Id, prefix = key.Prefix, name = key.Name }, actorId: callerId);
        }

        return new RevokeApiKeyResult(revoked);
    }
}
