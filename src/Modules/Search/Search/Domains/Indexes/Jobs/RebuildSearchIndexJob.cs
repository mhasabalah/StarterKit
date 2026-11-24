using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Search.Domains.Indexes.Models;
using Search.Observability;

namespace Search.Domains.Indexes.Jobs;

public enum RebuildOutcome
{
    /// <summary>The index is now built from the current source.</summary>
    Built,

    /// <summary>Already built from the current definition (and no rebuild was forced).</summary>
    UpToDate,

    /// <summary>Another worker holds the rebuild lease.</summary>
    Busy,

    /// <summary>Another worker took the lease over while this one was writing (it finishes the rebuild).</summary>
    LeaseLost,

    /// <summary>The rebuild failed; a previous build keeps answering.</summary>
    Failed,

    /// <summary>The index is not registered (any more).</summary>
    UnknownIndex
}

public sealed record RebuildResult(RebuildOutcome Outcome, int Documents = 0, int Swept = 0);

/// <summary>
/// Rebuilds one index of the current tenant (Hangfire restores the tenant of the enqueuer) on the <c>search</c> queue,
/// without downtime: it takes the lease, streams every document from the source in batches under a new stamp, sweeps
/// the documents the stamp did not reach, then makes the stamp live. Until then the previous build keeps answering,
/// and live changes during the rebuild carry the new stamp, so the sweep keeps them.
/// </summary>
/// <remarks>
/// No <c>DisableConcurrentExecution</c>: it would serialize every index of every tenant. The lease on
/// <see cref="SearchIndexState"/> serializes rebuilds of the same index only. An unavailable engine (or shutdown)
/// rethrows, so Hangfire retries; any other failure is recorded and not retried (it would fail the same way).
/// </remarks>
[Queue(SearchOptions.Queue)]
[AutomaticRetry(Attempts = 3)]
public sealed class RebuildSearchIndexJob(
    IUnitOfWorkRunner unitOfWork,
    SearchIndexRegistry registry,
    ITenantContext tenantContext,
    TimeProvider time,
    IOptions<SearchOptions> options,
    SearchMetrics metrics,
    ILogger<RebuildSearchIndexJob> logger)
{
    public async Task<RebuildResult> ExecuteAsync(string indexKey, bool force, IJobCancellationToken? cancellationToken)
    {
        if (registry.Find(indexKey) is not { } index)
        {
            logger.LogWarning("Rebuild of unknown search index {IndexKey} skipped", indexKey);
            return new RebuildResult(RebuildOutcome.UnknownIndex);
        }

        var tenantId = tenantContext.TenantId
                       ?? throw new InvalidOperationException("A search index is rebuilt in a tenant scope.");
        var settings = options.Value.Indexing;
        var lease = TimeSpan.FromMinutes(settings.LeaseMinutes);
        var startedAt = time.GetUtcNow().UtcDateTime;
        var stamp = startedAt.Ticks;

        if (await StartAsync(index, force, stamp, startedAt, lease) is { } notStarted)
            return Finish(index, new RebuildResult(notStarted));

        var scope = new IndexScope(index, tenantId);
        try
        {
            await unitOfWork.RunAsync((sp, ct) => sp.GetRequiredService<ISearchEngine>().EnsureIndexAsync(index, ct));

            var written = await StreamAsync(index, scope, stamp, lease, settings.BatchSize, cancellationToken);
            var swept = await unitOfWork.RunAsync((sp, ct) =>
                sp.GetRequiredService<ISearchEngine>().SweepAsync(scope, stamp, startedAt, ct));
            var count = await unitOfWork.RunAsync((sp, ct) => sp.GetRequiredService<ISearchEngine>().CountAsync(scope, ct));

            await WithStateAsync(index.Key, state => state.CompleteBuild(stamp, count, index.Fingerprint, time.GetUtcNow().UtcDateTime));
            logger.LogInformation("Search index {IndexKey} rebuilt: {Written} written, {Swept} swept, {Count} documents",
                index.Key, written, swept, count);
            return Finish(index, new RebuildResult(RebuildOutcome.Built, count, swept));
        }
        catch (LeaseLostException)
        {
            logger.LogWarning("Rebuild of search index {IndexKey} lost its lease to another worker", index.Key);
            return Finish(index, new RebuildResult(RebuildOutcome.LeaseLost));
        }
        catch (Exception ex)
        {
            var code = ex switch
            {
                SearchEngineUnavailableException => "engine_unavailable",
                OperationCanceledException => "cancelled",
                InvalidSearchDocumentException => "invalid_document",
                _ => "rebuild_failed"
            };
            await TryFailAsync(index.Key, stamp, code);

            if (ex is SearchEngineUnavailableException or OperationCanceledException)
                throw; // transient: Hangfire retries

            logger.LogError(ex, "Rebuild of search index {IndexKey} failed ({Code})", index.Key, code);
            return Finish(index, new RebuildResult(RebuildOutcome.Failed));
        }
    }

    /// <summary>Takes the lease (creating the state row on first use); <c>null</c> = started, otherwise why not.</summary>
    private async Task<RebuildOutcome?> StartAsync(RegisteredIndex index, bool force, long stamp, DateTime now, TimeSpan lease)
    {
        try
        {
            return await unitOfWork.RunAsync<RebuildOutcome?>(async (sp, ct) =>
            {
                var db = sp.GetRequiredService<SearchDbContext>();
                var state = await db.IndexStates.FirstOrDefaultAsync(s => s.IndexKey == index.Key, ct);
                if (state is null)
                {
                    state = SearchIndexState.Create(index.Key, now);
                    db.IndexStates.Add(state);
                }

                if (!force && !state.IsStale(index.Fingerprint))
                    return RebuildOutcome.UpToDate;
                if (!state.TryStartBuild(stamp, now, lease))
                    return RebuildOutcome.Busy;

                await db.SaveChangesAsync(ct);
                return null;
            });
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict() || IsUniqueViolation(ex))
        {
            return RebuildOutcome.Busy; // another worker created or took the state first
        }
    }

    private async Task<int> StreamAsync(
        RegisteredIndex index, IndexScope scope, long stamp, TimeSpan lease, int batchSize, IJobCancellationToken? cancellationToken)
    {
        var written = 0;
        // The source is read in one long-lived scope; every batch is written (and the lease extended) in its own.
        await unitOfWork.RunAsync(async (sp, ct) =>
        {
            var source = sp.GetRequiredKeyedService<ISearchDocumentSource>(index.Key);
            var batch = new List<SearchDocument>(batchSize);
            await foreach (var document in source.StreamAllAsync(ct))
            {
                cancellationToken?.ThrowIfCancellationRequested();
                batch.Add(document);
                if (batch.Count < batchSize)
                    continue;
                written += await WriteBatchAsync(index, scope, stamp, lease, batch);
                batch.Clear();
            }

            if (batch.Count > 0)
                written += await WriteBatchAsync(index, scope, stamp, lease, batch);
        });
        return written;
    }

    private async Task<int> WriteBatchAsync(RegisteredIndex index, IndexScope scope, long stamp, TimeSpan lease, List<SearchDocument> batch)
    {
        var prepared = batch.Select(d => SearchDocumentPreparer.Prepare(index, d)).ToList();
        await unitOfWork.RunAsync(async (sp, ct) =>
        {
            await sp.GetRequiredService<ISearchEngine>().UpsertAsync(scope, prepared, stamp, ct);
            var db = sp.GetRequiredService<SearchDbContext>();
            var state = await db.IndexStates.FirstAsync(s => s.IndexKey == index.Key, ct);
            if (!state.ExtendLease(stamp, time.GetUtcNow().UtcDateTime, lease))
                throw new LeaseLostException();
            await db.SaveChangesAsync(ct);
        });
        metrics.Indexed(index.Key, prepared.Count, 0);
        return prepared.Count;
    }

    private Task WithStateAsync(string indexKey, Action<SearchIndexState> change) =>
        unitOfWork.RunAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<SearchDbContext>();
            change(await db.IndexStates.FirstAsync(s => s.IndexKey == indexKey, ct));
            await db.SaveChangesAsync(ct);
        });

    private async Task TryFailAsync(string indexKey, long stamp, string code)
    {
        try
        {
            await unitOfWork.RunAsync(async (sp, ct) =>
            {
                var db = sp.GetRequiredService<SearchDbContext>();
                var state = await db.IndexStates.FirstAsync(s => s.IndexKey == indexKey, ct);
                if (state.BuildingStamp != stamp)
                    return; // another worker took over
                state.FailBuild(stamp, code);
                await db.SaveChangesAsync(ct);
            });
        }
        catch (Exception ex)
        {
            // The lease then simply expires; the next reconcile retries.
            logger.LogWarning(ex, "Could not record the failed rebuild of search index {IndexKey}", indexKey);
        }
    }

    private RebuildResult Finish(RegisteredIndex index, RebuildResult result)
    {
        metrics.Rebuilt(index.Key, result.Outcome.ToString().ToLowerInvariant());
        return result;
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } };

    private sealed class LeaseLostException() : Exception("The rebuild lease was taken over by another worker.");
}
