using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tenancy.Jobs;

/// <summary>
/// Daily: tenants whose deletion grace period is over are erased, one tenant per unit of work (a failure is logged
/// and retried next run; it never blocks other tenants):
/// <list type="number">
/// <item><see cref="TenantDataPurgeRequestedEvent"/> → modules erase what they own outside the databases (blobs…).</item>
/// <item>Tenant-data rows: a dedicated database we created is DROPPED; otherwise (shared, or an external dedicated
/// database) every tenant table is purged (<see cref="TenantDataPurger"/>).</item>
/// <item>Control-plane rows (branches, memberships, roles, invitations, audit) are deleted; the tenant row stays as
/// a <c>Deleted</c> tombstone (identifier stays reserved, history explains what happened).</item>
/// </list>
/// </summary>
public sealed class PurgeDeletedTenantsJob(
    IUnitOfWorkRunner runner,
    IServiceProvider services,
    TenantDataContextRegistry registry,
    ITenantConnectionStringResolver connectionResolver,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<PurgeDeletedTenantsJob> logger)
{
    public async Task ExecuteAsync(Hangfire.IJobCancellationToken cancellationToken)
    {
        var ct = cancellationToken.ShutdownToken;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var due = await runner.RunAsync<IReadOnlyList<TenantInfo>>(async (sp, token) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var rows = await sp.GetRequiredService<TenancyDbContext>().Tenants.AsNoTracking()
                    .Where(t => t.Status == TenantStatus.PendingDeletion && t.PurgeAfter != null && t.PurgeAfter <= now)
                    .ToListAsync(token);
                return rows.Select(t => t.ToInfo()).ToList();
            }
        }, ct);

        foreach (var tenant in due)
        {
            try
            {
                await PurgeAsync(tenant, ct);
                logger.LogWarning("Tenant {Tenant} ({TenantId}) purged", tenant.Identifier, tenant.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Purging tenant {Tenant} failed; retried on the next run", tenant.Identifier);
            }
        }
    }

    private async Task PurgeAsync(TenantInfo tenant, CancellationToken ct)
    {
        // 1. Modules erase external data (events dispatched after this unit commits).
        await runner.RunAsync(async (sp, token) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var db = sp.GetRequiredService<TenancyDbContext>();
                var row = await db.Tenants.FirstAsync(t => t.Id == tenant.Id, token);
                row.RequestDataPurge();
                await db.SaveChangesAsync(token);
            }
        }, ct);

        // 2. Tenant-data rows.
        if (tenant.IsDedicated && string.IsNullOrEmpty(tenant.ConnectionSecretRef))
        {
            var shared = configuration.GetConnectionString(Shared.Core.Data.DatabaseDefaults.ConnectionStringName)
                         ?? throw new InvalidOperationException("ConnectionStrings:starterkit is not configured.");
            await TenantDataPurger.DropDatabaseAsync(connectionResolver.Resolve(tenant, shared), ct);
        }
        else
        {
            foreach (var contextType in registry.ContextTypes)
            {
                using (TenantScope.BeginHostScope(tenant))
                {
                    await using var scope = services.CreateAsyncScope();
                    var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                    await TenantDataPurger.PurgeRowsAsync(context, tenant.Id, ct);
                }
            }
        }

        // 3. Control-plane rows + tombstone.
        await runner.RunAsync(async (sp, token) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var db = sp.GetRequiredService<TenancyDbContext>();
                await db.Invitations.Where(i => i.TenantId == tenant.Id).ExecuteDeleteAsync(token);
                await db.Memberships.Where(m => m.TenantId == tenant.Id).ExecuteDeleteAsync(token);
                await db.Roles.Where(r => r.TenantId == tenant.Id).ExecuteDeleteAsync(token);
                await db.AuditLogs.Where(a => a.TenantId == tenant.Id).ExecuteDeleteAsync(token);
                await db.Branches.Where(b => b.TenantId == tenant.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.ParentId, (Guid?)null), token);
                await db.Branches.Where(b => b.TenantId == tenant.Id).ExecuteDeleteAsync(token);

                var row = await db.Tenants.FirstAsync(t => t.Id == tenant.Id, token);
                row.MarkDeleted();
                await db.SaveChangesAsync(token);
            }
        }, ct);
    }
}
