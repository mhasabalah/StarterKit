using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Maintenance;

/// <param name="AbandonedUploadsClaimed">Uploading rows older than the timeout that were claimed for deletion.</param>
/// <param name="DeletionsCompleted">Rows whose blobs were deleted and that were removed (abandoned + retried).</param>
/// <param name="DeletionsFailed">Rows whose blob deletion failed again (rescheduled with backoff).</param>
/// <param name="StorageObjectsInspected">Objects older than the safety window that were checked against the DB.</param>
/// <param name="OrphansFound">Blobs that no row references.</param>
/// <param name="OrphansDeleted">Orphans deleted (always 0 in dry-run mode).</param>
/// <param name="PendingRowsPurged">Soft-deleted / associated pending-upload rows removed after the retention.</param>
public sealed record ReconciliationReport(
    int AbandonedUploadsClaimed,
    int DeletionsCompleted,
    int DeletionsFailed,
    int StorageObjectsInspected,
    int OrphansFound,
    int OrphansDeleted,
    int PendingRowsPurged);

/// <summary>
/// The DB/blob reconciliation sweeper (run by <c>AttachmentReconciliationJob</c>). Phases, each independent (a
/// failing phase is logged and the next one still runs):
/// <list type="number">
/// <item><b>Abandoned uploads</b>: <c>Uploading</c> rows older than <c>UploadingTimeoutMinutes</c> are claimed
/// (<c>RequestDeletion</c>, version-checked, so an upload finishing at that moment wins or loses cleanly) and
/// reaped: blob deleted, row removed.</item>
/// <item><b>Due deletions</b>: <c>PendingDeletion</c> rows whose next attempt is due (job never ran, or failed)
/// are reaped again; failures back off exponentially.</item>
/// <item><b>Storage scan</b> (optional, <c>StorageScan:Enabled</c>): blobs older than the safety window that no
/// attachment or pending row references are deleted, or only logged in dry-run mode.</item>
/// <item><b>Purge</b>: soft-deleted and associated pending-upload rows older than <c>PurgeRetentionDays</c> are
/// hard-deleted (their blobs are already gone or moved).</item>
/// </list>
/// All DB work goes through <see cref="IUnitOfWorkRunner"/>: one unit of work per row (or per read/purge batch).
/// <para>
/// <b>Multi-tenancy.</b> <see cref="RunAsync"/> visits every serviceable tenant in its own <see cref="TenantScope"/>
/// (right database, filters, RLS); one tenant's failure never stops the others. The storage scan only lists the
/// tenant's own key prefix (<c>t/{tenantId}/</c>; the Default tenant also owns legacy unprefixed keys) - it must
/// never judge another tenant's blobs against this tenant's rows (they would all look orphaned).
/// </para>
/// </summary>
public sealed class AttachmentReconciler(
    IUnitOfWorkRunner runner,
    IStorageService storage,
    IOptions<AttachmentOptions> options,
    TimeProvider time,
    ITenantStore tenantStore,
    ITenantContext tenantContext,
    ILogger<AttachmentReconciler> logger)
{
    private ReconciliationOptions Options => options.Value.Reconciliation;

    /// <summary>Reconciles every serviceable tenant; returns the summed report.</summary>
    public async Task<ReconciliationReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var total = new ReconciliationReport(0, 0, 0, 0, 0, 0, 0);

        foreach (var tenant in await tenantStore.GetServiceableTenantsAsync(cancellationToken))
        {
            using (TenantScope.Begin(tenant))
            using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenant.Id }))
            {
                try
                {
                    var r = await RunForCurrentTenantAsync(cancellationToken);
                    total = new ReconciliationReport(
                        total.AbandonedUploadsClaimed + r.AbandonedUploadsClaimed,
                        total.DeletionsCompleted + r.DeletionsCompleted,
                        total.DeletionsFailed + r.DeletionsFailed,
                        total.StorageObjectsInspected + r.StorageObjectsInspected,
                        total.OrphansFound + r.OrphansFound,
                        total.OrphansDeleted + r.OrphansDeleted,
                        total.PendingRowsPurged + r.PendingRowsPurged);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Reconciliation failed for tenant {TenantIdentifier}; continuing", tenant.Identifier);
                }
            }
        }

        return total;
    }

    /// <summary>Reconciles the CURRENT ambient tenant only.</summary>
    public async Task<ReconciliationReport> RunForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;

        var abandoned = await RunPhaseAsync("abandoned uploads", () => ReapAbandonedUploadsAsync(now, cancellationToken));
        var retried = await RunPhaseAsync("due deletions", () => RetryDueDeletionsAsync(now, cancellationToken));
        var scan = await RunPhaseAsync("storage scan", () => ScanStorageAsync(now, cancellationToken));
        var purged = await RunPhaseAsync("purge", () => PurgePendingRowsAsync(now, cancellationToken));

        var report = new ReconciliationReport(
            abandoned.Claimed,
            abandoned.Deleted + retried.Deleted,
            abandoned.Failed + retried.Failed,
            scan.Inspected,
            scan.Orphans,
            scan.Deleted,
            purged);

        return report;
    }

    // ----- 1. abandoned uploads -----

    public async Task<(int Claimed, int Deleted, int Failed)> ReapAbandonedUploadsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var cutoff = utcNow.AddMinutes(-Options.UploadingTimeoutMinutes);
        int claimed = 0, deleted = 0, failed = 0;

        await ForEachBatchAsync(
            (db, exclude, ct) => db.Attachments.IgnoreNonIsolationFilters()
                .Where(a => a.Status == AttachmentStatus.Uploading && !a.IsDeleted && a.CreatedAt < cutoff &&
                            !exclude.Contains(a.Id))
                .OrderBy(a => a.CreatedAt)
                .Select(a => a.Id)
                .Take(Options.BatchSize)
                .ToListAsync(ct),
            async id =>
            {
                var outcome = await runner.RunAsync<ReapOutcome?>(async (services, ct) =>
                {
                    var repository = services.GetRequiredService<IAttachmentRepository>();
                    var attachment = await repository.GetForMaintenanceAsync(id, ct);
                    if (attachment is not { Status: AttachmentStatus.Uploading })
                        return null; // finished or claimed meanwhile

                    // Claim first (version-checked): a MarkReady racing with us now fails instead of resurrecting
                    // a row whose blob we are about to delete.
                    attachment.RequestDeletion(utcNow);
                    await repository.SaveChangesAsync(ct);

                    return await services.GetRequiredService<AttachmentFileReaper>().ReapAsync(id, ct);
                }, cancellationToken);

                if (outcome is null)
                    return;
                claimed++;
                if (outcome is ReapOutcome.Deleted or ReapOutcome.AlreadyGone)
                    deleted++;
                if (outcome == ReapOutcome.Failed)
                    failed++;
            },
            cancellationToken);

        if (claimed > 0)
            logger.LogWarning("Reconciliation: {Count} abandoned upload(s) older than {Cutoff:o} removed", claimed, cutoff);

        return (claimed, deleted, failed);
    }

    // ----- 2. due deletions -----

    public async Task<(int Deleted, int Failed)> RetryDueDeletionsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        int deleted = 0, failed = 0;

        await ForEachBatchAsync(
            (db, exclude, ct) => db.Attachments.IgnoreNonIsolationFilters()
                .Where(a => a.Status == AttachmentStatus.PendingDeletion &&
                            (a.NextDeletionAttemptAt == null || a.NextDeletionAttemptAt <= utcNow) &&
                            !exclude.Contains(a.Id))
                .OrderBy(a => a.NextDeletionAttemptAt)
                .Select(a => a.Id)
                .Take(Options.BatchSize)
                .ToListAsync(ct),
            async id =>
            {
                var outcome = await runner.RunAsync(
                    (services, ct) => services.GetRequiredService<AttachmentFileReaper>().ReapAsync(id, ct),
                    cancellationToken);

                if (outcome == ReapOutcome.Deleted)
                    deleted++;
                if (outcome == ReapOutcome.Failed)
                    failed++;
            },
            cancellationToken);

        return (deleted, failed);
    }

    // ----- 3. storage scan -----

    public async Task<(int Inspected, int Orphans, int Deleted)> ScanStorageAsync(
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var scan = Options.StorageScan;
        if (!scan.Enabled)
            return (0, 0, 0);

        if (storage is not IStorageListing listing)
        {
            logger.LogWarning("Reconciliation: the storage provider cannot list objects; orphan scan skipped");
            return (0, 0, 0);
        }

        var tenantId = tenantContext.TenantId
                       ?? throw new TenantNotResolvedException("the storage scan runs per tenant.");

        // Only this tenant's keys. The Default tenant also owns legacy (pre-multi-tenancy) unprefixed keys, so it
        // lists everything and skips other tenants' roots.
        var prefix = tenantId == TenantDefaults.DefaultTenantId ? null : StoragePaths.TenantRoot(tenantId);

        var cutoff = new DateTimeOffset(utcNow, TimeSpan.Zero).AddHours(-scan.SafetyWindowHours);
        int seen = 0, inspected = 0, orphans = 0, deleted = 0;
        var batch = new List<string>(Options.BatchSize);

        await foreach (var item in listing.ListAsync(prefix, cancellationToken))
        {
            if (!StoragePaths.BelongsTo(item.StoragePath, tenantId))
                continue;

            if (++seen > scan.MaxObjectsPerRun)
                break;

            // Recently written blobs may belong to an upload whose row is not saved yet.
            if (item.LastModifiedUtc > cutoff)
                continue;

            batch.Add(item.StoragePath);
            if (batch.Count < Options.BatchSize)
                continue;

            var (o, d) = await HandleScanBatchAsync(batch, scan.DryRun, cancellationToken);
            inspected += batch.Count;
            orphans += o;
            deleted += d;
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            var (o, d) = await HandleScanBatchAsync(batch, scan.DryRun, cancellationToken);
            inspected += batch.Count;
            orphans += o;
            deleted += d;
        }

        if (orphans > 0)
        {
            logger.LogWarning("Reconciliation: {Orphans} orphan blob(s) found, {Deleted} deleted{DryRun}",
                orphans, deleted, scan.DryRun ? " (dry run)" : string.Empty);
        }

        return (inspected, orphans, deleted);
    }

    private async Task<(int Orphans, int Deleted)> HandleScanBatchAsync(
        List<string> paths, bool dryRun, CancellationToken cancellationToken)
    {
        var referenced = await runner.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<AttachmentDbContext>();
            // Any row in any status counts: never delete a blob something still points at.
            var hits = await db.Attachments.IgnoreNonIsolationFilters().Where(a => paths.Contains(a.StoragePath)).Select(a => a.StoragePath)
                .Concat(db.Attachments.IgnoreNonIsolationFilters().Where(a => a.ThumbnailPath != null && paths.Contains(a.ThumbnailPath)).Select(a => a.ThumbnailPath!))
                .Concat(db.PendingAttachments.IgnoreNonIsolationFilters().Where(p => paths.Contains(p.StoragePath)).Select(p => p.StoragePath))
                .Concat(db.PendingAttachments.IgnoreNonIsolationFilters().Where(p => p.ThumbnailPath != null && paths.Contains(p.ThumbnailPath)).Select(p => p.ThumbnailPath!))
                .ToListAsync(ct);
            return hits.ToHashSet(StringComparer.Ordinal);
        }, cancellationToken);

        var orphans = paths.Where(p => !referenced.Contains(p)).ToList();
        if (orphans.Count == 0)
            return (0, 0);

        if (dryRun)
        {
            foreach (var path in orphans)
                logger.LogInformation("Reconciliation (dry run): orphan blob {StoragePath} would be deleted", path);
            return (orphans.Count, 0);
        }

        var result = await storage.DeleteBatchAsync(orphans, cancellationToken);
        foreach (var path in orphans.Where(result.Succeeded))
            logger.LogInformation("Reconciliation: orphan blob {StoragePath} deleted", path);

        return (orphans.Count, orphans.Count - result.Failed.Count);
    }

    // ----- 4. purge -----

    public async Task<int> PurgePendingRowsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var cutoff = utcNow.AddDays(-Options.PurgeRetentionDays);
        var purged = 0;

        for (var batchNo = 0; batchNo < Options.MaxBatchesPerRun; batchNo++)
        {
            var count = await runner.RunAsync(async (services, ct) =>
            {
                var db = services.GetRequiredService<AttachmentDbContext>();
                var ids = await db.PendingAttachments.IgnoreNonIsolationFilters()
                    .Where(p => (p.IsDeleted || p.IsAssociated) && (p.LastModified ?? p.CreatedAt) < cutoff)
                    .OrderBy(p => p.Id)
                    .Select(p => p.Id)
                    .Take(Options.BatchSize)
                    .ToListAsync(ct);

                if (ids.Count == 0)
                    return 0;

                // Set-based: these rows raise no events and their blobs are already deleted or moved.
                return await db.PendingAttachments.IgnoreNonIsolationFilters()
                    .Where(p => ids.Contains(p.Id))
                    .ExecuteDeleteAsync(ct);
            }, cancellationToken);

            purged += count;
            if (count < Options.BatchSize)
                break;
        }

        if (purged > 0)
            logger.LogInformation("Reconciliation: {Count} old pending-upload row(s) purged", purged);

        return purged;
    }

    // ----- helpers -----

    /// <summary>
    /// Pages ids with <paramref name="nextBatch"/> (its own read-only unit of work) and handles each id; ids seen in
    /// this run are excluded from later batches so a row that keeps failing cannot spin the loop. A concurrency
    /// conflict on one row is logged and skipped.
    /// </summary>
    private async Task ForEachBatchAsync(
        Func<AttachmentDbContext, IReadOnlyCollection<Guid>, CancellationToken, Task<List<Guid>>> nextBatch,
        Func<Guid, Task> handle,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();

        for (var batchNo = 0; batchNo < Options.MaxBatchesPerRun; batchNo++)
        {
            var exclude = seen.ToList();
            var ids = await runner.RunAsync(
                (services, ct) => nextBatch(services.GetRequiredService<AttachmentDbContext>(), exclude, ct),
                cancellationToken);

            if (ids.Count == 0)
                return;

            foreach (var id in ids)
            {
                seen.Add(id);
                try
                {
                    await handle(id);
                }
                catch (Exception ex) when (ex.IsConcurrencyConflict())
                {
                    logger.LogInformation("Reconciliation: attachment {AttachmentId} changed concurrently; skipped", id);
                }
            }
        }
    }

    private async Task<T> RunPhaseAsync<T>(string phase, Func<Task<T>> run) where T : new()
    {
        try
        {
            return await run();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reconciliation phase '{Phase}' failed; the next run retries", phase);
            return new T();
        }
    }
}
