using UserDirectory.Contracts.Queries;
using Microsoft.Extensions.Options;
using Subscriptions.Contracts.Subscribers;

namespace Tenancy.Services;

/// <summary>
/// <see cref="ISubscriberIdProvider"/> that follows the mode at RUNTIME (<c>MultiTenancy:Enabled</c>):
/// <list type="bullet">
/// <item>multi-tenant: the subscriber is the CURRENT TENANT, and only members holding <c>tenant.billing.manage</c>
/// (owners included) may act for it;</item>
/// <item>single-tenant: the signed-in user (same as the Subscriptions default).</item>
/// </list>
/// </summary>
public sealed class TenantSubscriberIdProvider(
    ITenantContext tenantContext,
    IHttpContextService httpContextService,
    IOptions<MultiTenancyOptions> options) : ISubscriberIdProvider
{
    public Guid? GetCurrentSubscriberId()
    {
        if (!options.Value.Enabled)
            return httpContextService.GetUserIdAsGuid();

        if (tenantContext.TenantId is not { } tenantId)
            return null;

        if (tenantContext.Membership is not { } membership || membership.TenantId != tenantId)
            throw new TenantAccessDeniedException(TenantErrors.NotAMember, "only members can manage the tenant's subscription.");

        if (!membership.HasPermission(TenantPermissions.BillingManage))
            throw new ForbiddenException("Managing the subscription requires the 'tenant.billing.manage' permission.");

        return tenantId;
    }

    /// <summary>
    /// Multi-tenant: the current tenant, whoever the caller is (members without the billing permission, background
    /// jobs). Single-tenant: the signed-in or acting user.
    /// </summary>
    public Guid? GetPayingSubscriberId() =>
        options.Value.Enabled ? tenantContext.TenantId : httpContextService.GetUserIdAsGuid();
}

/// <summary>
/// Invoice/notification details of a subscriber: a TENANT (billing name = legal name, contact = owner) or, when the
/// id is not a tenant (single-tenant mode), the Identity user.
/// </summary>
public sealed class TenantSubscriberInfoProvider(
    TenancyDbContext dbContext,
    ISender sender,
    ILogger<TenantSubscriberInfoProvider> logger) : ISubscriberInfoProvider
{
    public async Task<SubscriberInfo?> GetAsync(Guid subscriberId, CancellationToken cancellationToken = default)
    {
        Tenant? tenant;
        using (TenantScope.BeginHostScope())
        {
            tenant = await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == subscriberId, cancellationToken);
        }

        if (tenant is not null)
        {
            var owner = tenant.OwnerUserId == Guid.Empty
                ? null
                : await sender.Send(new GetUserContactInfoQuery(tenant.OwnerUserId), cancellationToken);

            return new SubscriberInfo(tenant.Id, tenant.Name, owner?.Email, owner?.Phone, tenant.LegalName ?? tenant.Name);
        }

        try
        {
            var user = await sender.Send(new GetUserProfileQuery(subscriberId), cancellationToken);
            if (user is not null)
                return new SubscriberInfo(user.UserId, user.DisplayName, user.Email, user.Phone, user.DisplayName);

            logger.LogWarning("Subscriber {SubscriberId} is neither a tenant nor a user", subscriberId);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Subscriber {SubscriberId} could not be resolved through the user directory", subscriberId);
            return null;
        }
    }
}

/// <summary>
/// Multi-tenant billing notifications are addressed to the SUBSCRIBER (= the tenant): they go to the tenant's owner.
/// Ids that are not tenants (users, single-tenant mode) are left unchanged.
/// </summary>
public sealed class TenantNotificationRecipientResolver(
    TenancyDbContext dbContext,
    IOptions<MultiTenancyOptions> options) : Notification.Contracts.INotificationRecipientResolver
{
    public async Task<Guid?> ResolveUserIdAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
            return null;

        using (TenantScope.BeginHostScope())
        {
            var owner = await dbContext.Tenants.AsNoTracking()
                .Where(t => t.Id == recipientId)
                .Select(t => (Guid?)t.OwnerUserId)
                .FirstOrDefaultAsync(cancellationToken);
            return owner is { } id && id != Guid.Empty ? id : null;
        }
    }
}
