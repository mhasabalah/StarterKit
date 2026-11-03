using Hangfire;
using Microsoft.Extensions.Options;

namespace Tenancy.Services;

/// <summary>Drops cached tenant snapshots after any tenant change (by id, identifier and every custom host).</summary>
public sealed class TenantChangedEventHandler(TenancyDbContext dbContext, TenantStore store)
    : INotificationHandler<TenantChangedEvent>
{
    public async Task Handle(TenantChangedEvent notification, CancellationToken cancellationToken)
    {
        using (TenantScope.BeginHostScope())
        {
            var tenant = await dbContext.Tenants.AsNoTracking()
                .Where(t => t.Id == notification.TenantId)
                .Select(t => new { t.Identifier, Hosts = t.Domains.Select(d => d.Host).ToList() })
                .FirstOrDefaultAsync(cancellationToken);

            await store.InvalidateAsync(notification.TenantId, tenant?.Identifier, tenant?.Hosts ?? [],
                cancellationToken);
        }
    }
}

public sealed class TenantMembershipChangedEventHandler(ITenantMembershipResolver resolver)
    : INotificationHandler<TenantMembershipChangedEvent>
{
    public Task Handle(TenantMembershipChangedEvent notification, CancellationToken cancellationToken) =>
        resolver.InvalidateAsync(notification.TenantId, notification.UserId, cancellationToken);
}

public sealed class TenantRolesChangedEventHandler(ITenantMembershipResolver resolver)
    : INotificationHandler<TenantRolesChangedEvent>
{
    public Task Handle(TenantRolesChangedEvent notification, CancellationToken cancellationToken) =>
        resolver.InvalidateAsync(notification.TenantId, null, cancellationToken);
}

/// <summary>Kicks off provisioning (dedicated database / seeding) in the background after the tenant commit.</summary>
public sealed class TenantCreatedEventHandler(IBackgroundJobClient jobs, ILogger<TenantCreatedEventHandler> logger)
    : INotificationHandler<TenantCreatedEvent>
{
    public Task Handle(TenantCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Enqueued in HOST scope: provisioning chooses its own scopes (dedicated DB, then the tenant).
        using (TenantScope.BeginHostScope())
        {
            jobs.Enqueue<ProvisionTenantJob>(j => j.ExecuteAsync(notification.TenantId, JobCancellationToken.Null));
        }

        logger.LogInformation("Tenant {Identifier} created; provisioning enqueued", notification.Identifier);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Support impersonation is visible to the tenant: an entry in ITS audit trail (actor = the platform user). Runs inside
/// the issuing command - if this write fails, no impersonation token is handed out.
/// <para>
/// Deliberately an IN-PROCESS contract event, not an integration event: the audit write must succeed for the command
/// to succeed (audit-before-token). Asynchronous delivery would allow a token without an audit entry.
/// </para>
/// </summary>
public sealed class ImpersonationAuditHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : INotificationHandler<UserDirectory.Contracts.Events.ImpersonationStartedEvent>
{
    public async Task Handle(UserDirectory.Contracts.Events.ImpersonationStartedEvent notification,
        CancellationToken cancellationToken)
    {
        using (TenantScope.BeginHostScope())
        {
            audit.Record(TenantAuditActions.ImpersonationStarted, "Member", notification.TargetUserId.ToString(),
                new { notification.ImpersonatorUserId, notification.Reason, notification.ExpiresAt },
                notification.TenantId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
