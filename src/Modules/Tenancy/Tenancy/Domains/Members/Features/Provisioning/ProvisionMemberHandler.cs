using Subscriptions.Contracts.Subscriptions.Queries;
using Tenancy.Domains.Tenants.Exceptions;
using Tenancy.Services;

namespace Tenancy.Domains.Members.Features.Provisioning;

public class ProvisionMemberCommandValidator : AbstractValidator<ProvisionMemberCommand>
{
    public ProvisionMemberCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.RoleIds!.Count).LessThanOrEqualTo(50).When(x => x.RoleIds is not null);
    }
}

/// <summary>
/// Directory provisioning (enterprise SSO just-in-time, SCIM) in the current tenant. Called by Identity through
/// <see cref="ISender"/>; never exposed as an endpoint. Idempotent and safe to retry: an IdP that resends the same
/// user changes nothing. A new active member takes a seat, so the plan's <c>MaxUsers</c> is enforced
/// (403 <c>TenantLimitExceededException</c>, which the IdP reports back to the customer's admin).
/// </summary>
public class ProvisionMemberHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    ITenantAuditWriter audit,
    ISender sender,
    TimeProvider timeProvider,
    ILogger<ProvisionMemberHandler> logger) : ICommandHandler<ProvisionMemberCommand, ProvisionMemberResult>
{
    public async Task<ProvisionMemberResult> Handle(ProvisionMemberCommand command, CancellationToken cancellationToken)
    {
        var tenantId = request.TenantId;
        var roleIds = command.RoleIds is null ? null : await KnownRoleIdsAsync(command.RoleIds, cancellationToken);

        var membership = await dbContext.Memberships
            .FirstOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken);

        if (membership is null)
        {
            if (command.Active == false)
            {
                // Deprovisioning someone who never joined: nothing to do (SCIM "deactivate" before first sign-in).
                return new ProvisionMemberResult(command.UserId, Created: false, TenantMembershipStatus.Suspended);
            }

            await EnsureSeatAvailableAsync(tenantId, cancellationToken);

            membership = TenantMembership.Create(tenantId, command.UserId, roleIds ?? [], [],
                timeProvider.GetUtcNow().UtcDateTime);
            dbContext.Memberships.Add(membership);
            audit.Record(TenantAuditActions.MemberProvisioned, "Member", command.UserId.ToString(),
                new { source = command.Source.ToString(), roleIds });

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Member {UserId} provisioned in tenant {TenantId} by {Source}",
                command.UserId, tenantId, command.Source);
            return new ProvisionMemberResult(command.UserId, Created: true, membership.Status);
        }

        var changed = false;

        if (roleIds is not null && !membership.RoleIds.ToHashSet().SetEquals(roleIds))
        {
            membership.SetRoles(roleIds);
            changed = true;
        }

        if (command.Active is { } active && active != (membership.Status == TenantMembershipStatus.Active))
        {
            if (active)
            {
                await EnsureSeatAvailableAsync(tenantId, cancellationToken);
                membership.Activate();
                changed = true;
            }
            else if (membership.IsOwner)
            {
                // A directory never locks the owner out of their own tenant (break-glass account).
                logger.LogWarning("Directory tried to suspend the owner {UserId} of tenant {TenantId}; ignored",
                    command.UserId, tenantId);
            }
            else
            {
                membership.Suspend();
                changed = true;
            }
        }

        if (!changed)
            return new ProvisionMemberResult(command.UserId, Created: false, membership.Status);

        audit.Record(
            membership.Status == TenantMembershipStatus.Active
                ? TenantAuditActions.MemberProvisioned
                : TenantAuditActions.MemberDeprovisioned,
            "Member", command.UserId.ToString(),
            new { source = command.Source.ToString(), status = membership.Status.ToString(), roleIds });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProvisionMemberResult(command.UserId, Created: false, membership.Status);
    }

    private async Task<List<Guid>> KnownRoleIdsAsync(IReadOnlyList<Guid> requested, CancellationToken ct)
    {
        var wanted = requested.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0)
            return [];

        return await dbContext.Roles.Where(r => wanted.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
    }

    private async Task EnsureSeatAvailableAsync(Guid tenantId, CancellationToken ct)
    {
        if (!request.MultiTenancyEnabled)
            return;

        int? maxUsers;
        try
        {
            maxUsers = (await sender.Send(new GetActivePlanLimitsQuery(tenantId), ct))?.MaxUsers;
        }
        catch (NotFoundException)
        {
            maxUsers = null;
        }

        if (maxUsers is not { } max)
            return;

        var active = await dbContext.Memberships.CountAsync(m => m.Status == TenantMembershipStatus.Active, ct);
        if (active >= max)
            throw new TenantLimitExceededException($"The plan allows {max} users; the directory tried to add more.");
    }
}
