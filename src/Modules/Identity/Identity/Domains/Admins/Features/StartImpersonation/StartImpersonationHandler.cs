using Identity.Contracts.Impersonation;
using UserDirectory.Contracts.Events;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Services.Token;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.StartImpersonation;

public class StartImpersonationCommandValidator : AbstractValidator<StartImpersonationCommand>
{
    public StartImpersonationCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(500)
            .WithMessage("A justification (10-500 characters, e.g. a ticket reference) is required.");
        RuleFor(x => x.Minutes).InclusiveBetween(1, 60).When(x => x.Minutes.HasValue);
    }
}

/// <summary>
/// Issues a support impersonation token (see <see cref="StartImpersonationCommand"/>). Guard rails:
/// <list type="bullet">
/// <item>feature switch <c>MultiTenancy:Impersonation:Enabled</c>; caller holds <c>tenants.impersonate</c> (+ 2FA rules);</item>
/// <item>never yourself, never a staff account (no privilege escalation through impersonation), never an
/// inactive/locked user;</item>
/// <item>multi-tenant: the target must be an active member of a serviceable tenant - the token carries THAT membership
/// (tenant permissions are the target's, nothing more);</item>
/// <item>access token only (no refresh token), lifetime ≤ <c>MaxMinutes</c>; restrictions enforced per request by
/// <c>ImpersonationGuardMiddleware</c>;</item>
/// <item>audited atomically in the Identity admin audit (IAuditedCommand) and in the tenant's audit trail
/// (<see cref="ImpersonationStartedEvent"/>) - a failing tenant audit fails the whole command.</item>
/// </list>
/// </summary>
public class StartImpersonationHandler(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator tokenGenerator,
    IIdentityAuditWriter audit,
    IHttpContextService httpContextService,
    IPublisher publisher,
    ITenantStore tenantStore,
    ITenantMembershipResolver memberships,
    IOptions<MultiTenancyOptions> options,
    TimeProvider timeProvider,
    ILogger<StartImpersonationHandler> logger) : ICommandHandler<StartImpersonationCommand, StartImpersonationResult>
{
    public async Task<StartImpersonationResult> Handle(StartImpersonationCommand command, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Impersonation.Enabled)
            throw new ForbiddenException("Impersonation is disabled.");

        var actorId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Authentication is required.");
        if (actorId == command.UserId)
            throw new BadRequestException("You cannot impersonate yourself.");
        if (!string.IsNullOrEmpty(httpContextService.GetClaimValue(TenantClaimTypes.Actor)))
            throw new ForbiddenException("Impersonation sessions cannot start another impersonation.");

        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is not { IsActive: true } || await userManager.IsLockedOutAsync(user))
            throw new NotFoundException("User", command.UserId);

        var roles = (await userManager.GetRolesAsync(user)).ToList();
        if (roles.Any(RoleConstants.IsStaffRole))
            throw new ForbiddenException("Staff accounts cannot be impersonated.");

        TenantMembershipInfo? membership = null;
        var tenantId = command.TenantId;
        if (settings.Enabled)
        {
            var tenant = await tenantStore.FindByIdAsync(tenantId, cancellationToken);
            if (tenant is not { Status: TenantStatus.Active or TenantStatus.Suspended or TenantStatus.Migrating })
                throw new NotFoundException("Tenant", tenantId);

            membership = await memberships.GetMembershipAsync(user.Id, tenantId, cancellationToken)
                         ?? throw new NotFoundException("The user is not an active member of this tenant.");
        }
        else
        {
            tenantId = TenantDefaults.DefaultTenantId;
        }

        var minutes = Math.Min(command.Minutes ?? settings.Impersonation.MaxMinutes, settings.Impersonation.MaxMinutes);
        var expiresAt = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(minutes);
        var token = await tokenGenerator.GenerateTokenAsync(user, roles, multiFactor: false, membership,
            new ImpersonationGrant(actorId, expiresAt));

        audit.Record(IdentityAuditActions.ImpersonationStarted, user.Id, reason: command.Reason,
            after: new { TenantId = tenantId, ExpiresAt = expiresAt });

        // Inside the audited transaction: if the tenant audit cannot be written, no token is handed out.
        await publisher.Publish(new ImpersonationStartedEvent(tenantId, actorId, user.Id, command.Reason, expiresAt),
            cancellationToken);

        logger.LogWarning("User {ActorId} impersonates {UserId} in tenant {TenantId} until {ExpiresAt}: {Reason}",
            actorId, user.Id, tenantId, expiresAt, command.Reason);

        return new StartImpersonationResult(token, expiresAt, tenantId, user.Id);
    }
}
