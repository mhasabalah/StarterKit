using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tenancy.Services;

/// <summary>
/// Automated move of a tenant's DATA PLANE between databases (shared ⇄ dedicated, or dedicated → another server).
/// Started by <c>POST /host/tenants/{id}/move</c> (tenant → <see cref="TenantStatus.Migrating"/>, read-only), run by
/// <see cref="MoveTenantJob"/>:
/// <list type="number">
/// <item>Wait one cache period so every instance sees <c>Migrating</c> (writes 503, jobs/messages deferred).</item>
/// <item>Dedicated target: create + migrate its database (no seeding - rows are copied).</item>
/// <item>Per tenant-data context: copy + verify the rows in one target transaction (<see cref="TenantDataMover"/>).</item>
/// <item>Switch: the tenant now points at the target (status restored; caches dropped by <c>TenantChangedEvent</c>).</item>
/// <item>Wait one cache period (instances still reading the source), then clean the source: a dedicated database
/// we created is dropped, otherwise the tenant's rows are purged.</item>
/// </list>
/// Failure before the switch: the tenant stays where it was (status restored) and the target's partial copy is removed.
/// Every step is idempotent, so a crashed move is simply retried (<c>POST /host/tenants/{id}/move/retry</c>).
/// Blobs are not affected (storage is not per database).
/// </summary>
public sealed class TenantMover(
    IServiceProvider services,
    IUnitOfWorkRunner runner,
    TenantDataContextRegistry registry,
    ITenantConnectionStringResolver connectionResolver,
    IConfiguration configuration,
    IOptions<MultiTenancyOptions> options,
    ILogger<TenantMover> logger)
{
    /// <summary>Overridable in tests; defaults to the L1 cache lifetime + 5 s.</summary>
    internal TimeSpan? PropagationDelayOverride { get; set; }

    private TimeSpan PropagationDelay =>
        PropagationDelayOverride ?? TimeSpan.FromSeconds(Math.Max(0, options.Value.Cache.LocalSeconds) + 5);

    public async Task MoveAsync(Guid tenantId, CancellationToken ct)
    {
        var state = await LoadAsync(tenantId, ct);
        if (state is null)
        {
            logger.LogWarning("Tenant move skipped: tenant {TenantId} is not migrating", tenantId);
            return;
        }

        var (source, target) = state.Value;
        var shared = configuration.GetConnectionString("starterkit")
                     ?? throw new InvalidOperationException("ConnectionStrings:starterkit is not configured.");
        logger.LogWarning("Moving tenant {Tenant} from {From} to {To}", source.Identifier, Describe(source), Describe(target));

        // Safety net (also checked when the move is requested): never "move" onto the live database - the failure
        // cleanup would then erase the tenant's real data.
        string sourceConnection, targetConnection;
        try
        {
            sourceConnection = connectionResolver.Resolve(source, shared);
            targetConnection = connectionResolver.Resolve(target, shared);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant move of {Tenant} aborted: a connection cannot be resolved", source.Identifier);
            await TryAsync(() => FinishAsync(tenantId, t => t.FailMove(), TenantAuditActions.TenantMoveFailed,
                new { To = Describe(target), Error = ex.Message }, CancellationToken.None), "restoring the tenant status");
            return;
        }

        if (TenantDataMover.SameDatabase(sourceConnection, targetConnection))
        {
            logger.LogError("Tenant move of {Tenant} aborted: source and target are the same database", source.Identifier);
            await TryAsync(() => FinishAsync(tenantId, t => t.FailMove(), TenantAuditActions.TenantMoveFailed,
                new { To = Describe(target), Error = "Source and target are the same database." }, CancellationToken.None),
                "restoring the tenant status");
            return;
        }

        IReadOnlyDictionary<string, long> copied;
        try
        {
            await Task.Delay(PropagationDelay, ct);

            if (target.IsDedicated)
            {
                foreach (var contextType in registry.ContextTypes)
                {
                    await Shared.Core.Data.Extensions.MigrateTenantDatabaseAsync(services, contextType, target, ct);
                }
            }

            var all = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var contextType in registry.ContextTypes)
            {
                foreach (var (table, rows) in await CopyContextAsync(contextType, source, target, ct)) all[table] = rows;
            }

            copied = all;
            await FinishAsync(tenantId, t => t.CompleteMove(), TenantAuditActions.TenantMoveCompleted,
                new { From = Describe(source), To = Describe(target), Rows = copied }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Moving tenant {Tenant} failed; it stays in {From}", source.Identifier, Describe(source));
            await TryAsync(() => FinishAsync(tenantId, t => t.FailMove(), TenantAuditActions.TenantMoveFailed,
                new { To = Describe(target), Error = ex.GetType().Name + ": " + ex.Message }, CancellationToken.None),
                "restoring the tenant status");
            await TryAsync(() => CleanAsync(target, shared, CancellationToken.None), "removing the partial copy");
            return;
        }

        logger.LogWarning("Tenant {Tenant} moved to {To} ({Tables} tables, {Rows} rows)", source.Identifier,
            Describe(target), copied.Count, copied.Values.Sum());

        // Instances may still read the source until their L1 cache expires.
        await Task.Delay(PropagationDelay, ct);
        await TryAsync(() => CleanAsync(source, shared, ct), "cleaning the source database");
    }

    private async Task<IReadOnlyDictionary<string, long>> CopyContextAsync(Type contextType, TenantInfo source,
        TenantInfo target, CancellationToken ct)
    {
        // Each context is created (connection chosen) and opened inside the host scope of ITS database.
        AsyncServiceScope sourceScope, targetScope;
        DbContext sourceContext, targetContext;
        using (TenantScope.BeginHostScope(source))
        {
            sourceScope = services.CreateAsyncScope();
            sourceContext = (DbContext)sourceScope.ServiceProvider.GetRequiredService(contextType);
            await sourceContext.Database.OpenConnectionAsync(ct);
        }

        await using (sourceScope)
        {
            using (TenantScope.BeginHostScope(target))
            {
                targetScope = services.CreateAsyncScope();
                targetContext = (DbContext)targetScope.ServiceProvider.GetRequiredService(contextType);
                await targetContext.Database.OpenConnectionAsync(ct);
            }

            await using (targetScope)
            {
                return await TenantDataMover.CopyAsync(sourceContext, targetContext, source.Id, ct);
            }
        }
    }

    /// <summary>Removes the tenant's data from a database it no longer lives in.</summary>
    private async Task CleanAsync(TenantInfo location, string shared, CancellationToken ct)
    {
        if (location.IsDedicated && string.IsNullOrEmpty(location.ConnectionSecretRef))
        {
            // A database we created for this tenant only.
            await TenantDataPurger.DropDatabaseAsync(connectionResolver.Resolve(location, shared), ct);
            return;
        }

        foreach (var contextType in registry.ContextTypes)
        {
            using (TenantScope.BeginHostScope(location))
            {
                await using var scope = services.CreateAsyncScope();
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                if (!await context.Database.CanConnectAsync(ct)) continue; // never created
                await TenantDataPurger.PurgeRowsAsync(context, location.Id, ct);
            }
        }
    }

    private Task<(TenantInfo Source, TenantInfo Target)?> LoadAsync(Guid tenantId, CancellationToken ct) =>
        runner.RunAsync<(TenantInfo, TenantInfo)?>(async (sp, token) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var tenant = await sp.GetRequiredService<TenancyDbContext>().Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId, token);
                if (tenant is not { Status: TenantStatus.Migrating, MoveTargetIsolation: not null }) return null;
                return (tenant.ToInfo(), tenant.ToMoveTargetInfo());
            }
        }, ct);

    private Task FinishAsync(Guid tenantId, Action<Tenant> change, string action, object data, CancellationToken ct) =>
        runner.RunAsync(async (sp, token) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var db = sp.GetRequiredService<TenancyDbContext>();
                var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId, token);
                change(tenant); // throws when the move was aborted meanwhile → treated as a failure
                sp.GetRequiredService<ITenantAuditWriter>()
                    .Record(action, nameof(Tenant), tenantId.ToString(), data, tenantId);
                await db.SaveChangesAsync(token);
            }
        }, ct);

    private async Task TryAsync(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant move: {Step} failed - manual cleanup may be needed " +
                                "(see wiki/MultiTenancy/Operations.md)", what);
        }
    }

    private static string Describe(TenantInfo tenant) =>
        tenant.IsDedicated ? $"Dedicated({tenant.ConnectionSecretRef ?? "own database"})" : "Shared";
}

/// <summary>
/// Hangfire entry point of <see cref="TenantMover"/>. Moves are serialized (one at a time) and never retried
/// automatically: a failed move restores the tenant and is re-run explicitly.
/// </summary>
public sealed class MoveTenantJob(TenantMover mover)
{
    [Hangfire.AutomaticRetry(Attempts = 0)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public Task ExecuteAsync(Guid tenantId, Hangfire.IJobCancellationToken cancellationToken) =>
        mover.MoveAsync(tenantId, cancellationToken.ShutdownToken);
}
