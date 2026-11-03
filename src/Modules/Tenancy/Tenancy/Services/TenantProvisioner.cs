using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tenancy.Services;

/// <summary>
/// Prepares the DATA PLANE of a tenant and activates it:
/// <list type="bullet">
/// <item>Dedicated: creates (via EF Migrate) and migrates the tenant database for every
/// <see cref="ITenantDataContext"/>, then runs their seeders as the tenant.</item>
/// <item>Shared: runs the tenant-data seeders as the tenant (schema already exists).</item>
/// </list>
/// Idempotent: migrations and seeders are re-runnable, so a failed provisioning is simply retried.
/// </summary>
public sealed class TenantProvisioner(
    IServiceProvider services,
    IUnitOfWorkRunner runner,
    TenantDataContextRegistry registry,
    ILogger<TenantProvisioner> logger)
{
    public async Task ProvisionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            logger.LogWarning("Provisioning skipped: tenant {TenantId} does not exist", tenantId);
            return;
        }

        if (tenant.Status is TenantStatus.Deleted or TenantStatus.PendingDeletion)
        {
            logger.LogInformation("Provisioning skipped: tenant {Tenant} is {Status}", tenant.Identifier, tenant.Status);
            return;
        }

        try
        {
            foreach (var contextType in registry.ContextTypes)
            {
                if (tenant.IsDedicated)
                {
                    await Shared.Core.Data.Extensions.MigrateAndSeedAsync(services, contextType, tenant, cancellationToken);
                }
                else
                {
                    await Shared.Core.Data.Extensions.SeedAsync(services, contextType, tenant);
                }
            }

            await UpdateAsync(tenantId, t => t.MarkProvisioned(), cancellationToken);
            logger.LogInformation("Tenant {Tenant} provisioned ({Isolation})", tenant.Identifier, tenant.Isolation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Provisioning tenant {Tenant} failed", tenant.Identifier);
            await UpdateAsync(tenantId, t => t.MarkProvisioningFailed(ex.GetType().Name + ": " + ex.Message),
                cancellationToken);
            throw;
        }
    }

    private Task<TenantInfo?> LoadAsync(Guid tenantId, CancellationToken cancellationToken) =>
        runner.RunAsync(async (sp, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var tenant = await sp.GetRequiredService<TenancyDbContext>().Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
                return tenant?.ToInfo();
            }
        }, cancellationToken);

    private Task UpdateAsync(Guid tenantId, Action<Tenant> change, CancellationToken cancellationToken) =>
        runner.RunAsync(async (sp, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var db = sp.GetRequiredService<TenancyDbContext>();
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
                if (tenant is null) return;
                change(tenant);
                await db.SaveChangesAsync(ct);
            }
        }, cancellationToken);
}

/// <summary>Hangfire entry point for <see cref="TenantProvisioner"/> (automatic retries on failure).</summary>
public sealed class ProvisionTenantJob(TenantProvisioner provisioner)
{
    [Hangfire.AutomaticRetry(Attempts = 5)]
    public Task ExecuteAsync(Guid tenantId, Hangfire.IJobCancellationToken cancellationToken) =>
        provisioner.ProvisionAsync(tenantId, cancellationToken.ShutdownToken);
}

/// <summary>
/// Startup migrator for DEDICATED tenant databases (the shared database is migrated by <c>UseMigration</c>).
/// Bounded parallelism (<c>MultiTenancy:Database:MigrationParallelism</c>); one tenant's failure is logged and does
/// not stop the others or the app. Runs in the background so a large fleet does not delay startup.
/// </summary>
public sealed class DedicatedTenantDatabaseMigrator(
    IServiceProvider services,
    IUnitOfWorkRunner runner,
    TenantDataContextRegistry registry,
    IOptions<MultiTenancyOptions> options,
    IConfiguration configuration,
    ILogger<DedicatedTenantDatabaseMigrator> logger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || registry.ContextTypes.Count == 0) return;
        if (configuration.GetValue("MultiTenancy:Database:MigrateDedicatedOnStartup", true) is false) return;

        var tenants = await runner.RunAsync<IReadOnlyList<TenantInfo>>(async (sp, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var rows = await sp.GetRequiredService<TenancyDbContext>().Tenants.AsNoTracking()
                    .Where(t => t.Isolation == TenantIsolation.Dedicated &&
                                (t.Status == TenantStatus.Active || t.Status == TenantStatus.Suspended))
                    .ToListAsync(ct);
                return rows.Select(t => t.ToInfo()).ToList();
            }
        }, stoppingToken);

        if (tenants.Count == 0) return;
        logger.LogInformation("Migrating {Count} dedicated tenant database(s)", tenants.Count);

        var failed = 0;
        await Parallel.ForEachAsync(tenants,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Value.Database.MigrationParallelism,
                CancellationToken = stoppingToken
            },
            async (tenant, ct) =>
            {
                try
                {
                    foreach (var contextType in registry.ContextTypes)
                    {
                        await Shared.Core.Data.Extensions.MigrateAndSeedAsync(services, contextType, tenant, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                    logger.LogError(ex, "Migrating the database of tenant {Tenant} failed; it keeps its previous schema",
                        tenant.Identifier);
                }
            });

        logger.LogInformation("Dedicated tenant databases migrated ({Failed} failed)", failed);
    }
}
