using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Search.Contracts.Indexing.IntegrationEvents;
using Search.Domains.Indexes.Features.ApplySearchDocumentChanges;
using Search.Observability;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Search.Domains.Indexes.Jobs;

/// <summary>What the drift check found in one index of one tenant.</summary>
/// <param name="Repaired">Documents re-indexed or removed (with <c>Search:Drift:AutoRepair</c>).</param>
public sealed record DriftReport(string IndexKey, int Missing, int Stale, int Orphaned, int Repaired, bool Skipped = false);

/// <summary>
/// Recurring (<c>Search:Drift:Cron</c>): compares every built index with its source and records the differences on the
/// index state: <b>missing</b> (in the source, not indexed), <b>stale</b> (indexed with another version) and
/// <b>orphaned</b> (indexed, gone from the source). With <c>Search:Drift:AutoRepair</c> it re-indexes them from the
/// source. Catches what the outbox cannot: a change raised without its event, a failed consumer past its retries.
/// Indexes above <c>Search:Drift:MaxDocuments</c> are skipped (rebuild them instead).
/// </summary>
public sealed class DetectSearchDriftJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    SearchIndexRegistry registry,
    ITenantContext tenantContext,
    TimeProvider time,
    IOptions<SearchOptions> options,
    SearchMetrics metrics,
    ILogger<DetectSearchDriftJob> logger)
{
    [DisableConcurrentExecution(60)]
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var drifted = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) =>
        {
            foreach (var report in await CheckCurrentTenantAsync(null, cancellationToken))
                drifted += report.Missing + report.Stale + report.Orphaned;
        }, nameof(DetectSearchDriftJob));
        return drifted;
    }

    /// <summary>
    /// One index of the CURRENT tenant, on request (<c>POST /search/indexes/{key}/drift-check</c>), on the
    /// <c>search</c> queue. Returns the documents found out of sync.
    /// </summary>
    [Queue(SearchOptions.Queue)]
    public async Task<int> CheckIndexAsync(string indexKey, IJobCancellationToken? cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexKey);
        var reports = await CheckCurrentTenantAsync(indexKey, cancellationToken);
        return reports.Sum(report => report.Missing + report.Stale + report.Orphaned);
    }

    /// <summary>Checks the built indexes of the CURRENT tenant (all of them, or only <paramref name="indexKey"/>).</summary>
    public async Task<IReadOnlyList<DriftReport>> CheckCurrentTenantAsync(string? indexKey, IJobCancellationToken? cancellationToken)
    {
        var tenantId = tenantContext.TenantId
                       ?? throw new InvalidOperationException("Search drift is checked in a tenant scope.");
        var built = await unitOfWork.RunAsync((sp, ct) => sp.GetRequiredService<SearchDbContext>().IndexStates.AsNoTracking()
            .Where(s => s.LastBuiltAt != null)
            .Select(s => s.IndexKey)
            .ToListAsync(ct));

        var reports = new List<DriftReport>();
        foreach (var index in registry.Indexes.Where(i => built.Contains(i.Key) && (indexKey is null || i.Key == indexKey)))
        {
            cancellationToken?.ThrowIfCancellationRequested();
            try
            {
                reports.Add(await CheckAsync(index, new IndexScope(index, tenantId)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Drift check of search index {IndexKey} failed", index.Key);
            }
        }

        return reports;
    }

    private async Task<DriftReport> CheckAsync(RegisteredIndex index, IndexScope scope)
    {
        var settings = options.Value.Drift;
        var count = await unitOfWork.RunAsync((sp, ct) => sp.GetRequiredService<ISearchEngine>().CountAsync(scope, ct));
        if (count > settings.MaxDocuments)
        {
            logger.LogWarning("Drift check of search index {IndexKey} skipped: {Count} documents (limit {Limit}); rebuild it instead",
                index.Key, count, settings.MaxDocuments);
            return new DriftReport(index.Key, 0, 0, 0, 0, Skipped: true);
        }

        var indexed = await unitOfWork.RunAsync(async (sp, ct) =>
        {
            var stamps = new Dictionary<string, long>(StringComparer.Ordinal);
            await foreach (var stamp in sp.GetRequiredService<ISearchEngine>().StreamStampsAsync(scope, ct))
                stamps[stamp.Id] = stamp.Version;
            return stamps;
        });

        var (missing, stale) = (new List<string>(), new List<string>());
        await unitOfWork.RunAsync(async (sp, ct) =>
        {
            var source = sp.GetRequiredKeyedService<ISearchDocumentSource>(index.Key);
            await foreach (var stamp in source.StreamStampsAsync(ct))
            {
                if (!indexed.Remove(stamp.Id, out var version))
                    missing.Add(stamp.Id);
                else if (version != stamp.Version)
                    stale.Add(stamp.Id);
            }
        });
        var orphaned = indexed.Keys.ToList();

        await unitOfWork.RunAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<SearchDbContext>();
            var state = await db.IndexStates.FirstAsync(s => s.IndexKey == index.Key, ct);
            state.RecordDrift(missing.Count, stale.Count, orphaned.Count, time.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        });
        metrics.Drift(index.Key, missing.Count, stale.Count, orphaned.Count);

        var repaired = 0;
        if (settings.AutoRepair)
        {
            // Re-indexing from the source fixes all three: present ids are written, absent ones removed.
            foreach (var chunk in missing.Concat(stale).Concat(orphaned).Chunk(SearchDocumentsChangedIntegrationEvent.MaxDocuments))
            {
                var result = await unitOfWork.RunAsync((sp, ct) => sp.GetRequiredService<ISender>()
                    .Send(new ApplySearchDocumentChangesCommand(index.Key, chunk), ct));
                repaired += result.Upserted + result.Deleted;
            }
        }

        if (missing.Count + stale.Count + orphaned.Count > 0)
        {
            logger.LogWarning(
                "Search index {IndexKey} drifted: {Missing} missing, {Stale} stale, {Orphaned} orphaned; {Repaired} repaired",
                index.Key, missing.Count, stale.Count, orphaned.Count, repaired);
        }

        return new DriftReport(index.Key, missing.Count, stale.Count, orphaned.Count, repaired);
    }
}
