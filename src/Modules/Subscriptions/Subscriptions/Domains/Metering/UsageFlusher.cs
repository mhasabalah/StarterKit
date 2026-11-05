using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Domains.Metering;

/// <summary>
/// Persists dirty usage counters to <c>usage_records</c> (an upsert of the absolute value: running twice is harmless).
/// A key is drained from the dirty set BEFORE its value is read, so an increment that lands in between marks it dirty
/// again and is picked up by the next run. A key whose save fails is put back.
/// </summary>
public interface IUsageFlusher
{
    /// <returns>The number of counters written.</returns>
    Task<int> FlushAsync(int batchSize, CancellationToken ct = default);
}

public sealed class UsageFlusher(
    IUsageCounterStore store,
    IUnitOfWorkRunner unitOfWork,
    TimeProvider timeProvider,
    ILogger<UsageFlusher> logger) : IUsageFlusher
{
    public async Task<int> FlushAsync(int batchSize, CancellationToken ct = default)
    {
        var written = 0;
        while (!ct.IsCancellationRequested)
        {
            var keys = await store.DrainDirtyAsync(batchSize, ct);
            if (keys.Count == 0)
                break;

            var counters = new List<(Guid SubscriberId, string PeriodKey, string Feature, long Quantity, string Key)>();
            foreach (var key in keys)
            {
                if (!UsagePeriods.TryParseCounterKey(key, out var subscriberId, out var periodKey, out var feature))
                    continue;

                if (await store.GetAsync(key, ct) is { } quantity)
                    counters.Add((subscriberId, periodKey, feature, quantity, key));
            }

            try
            {
                await unitOfWork.RunAsync((services, token) => SaveAsync(services, counters, token), ct);
                written += counters.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Flushing {Count} usage counters failed; they stay queued", counters.Count);
                await store.MarkDirtyAsync(keys, ct);
                break;
            }
        }

        return written;
    }

    private async Task SaveAsync(
        IServiceProvider services,
        List<(Guid SubscriberId, string PeriodKey, string Feature, long Quantity, string Key)> counters,
        CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var now = timeProvider.GetUtcNow();

        // ONE query for the whole batch, then upsert from a dictionary (no query per counter).
        var subscriberIds = counters.Select(c => c.SubscriberId).Distinct().ToList();
        var features = counters.Select(c => c.Feature).Distinct().ToList();
        var periodKeys = counters.Select(c => c.PeriodKey).Distinct().ToList();
        var existing = (await dbContext.UsageRecords
                .Where(x => subscriberIds.Contains(x.SubscriberId) && features.Contains(x.Key) && periodKeys.Contains(x.PeriodKey))
                .ToListAsync(ct))
            .ToDictionary(x => (x.SubscriberId, x.Key, x.PeriodKey));

        foreach (var counter in counters)
        {
            if (existing.TryGetValue((counter.SubscriberId, counter.Feature, counter.PeriodKey), out var record))
                record.Set(counter.Quantity, now);
            else
                dbContext.UsageRecords.Add(UsageRecord.Create(counter.SubscriberId, counter.Feature, counter.PeriodKey, counter.Quantity, now));
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
