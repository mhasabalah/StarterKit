using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Entitlements;
using Subscriptions.Options;

namespace Subscriptions.Domains.Metering;

/// <summary>
/// Usage metering and quota enforcement for the current subscriber, behind the <see cref="IUsageMeter"/> and
/// <see cref="IQuotaService"/> ports of Shared.Core.
/// <list type="bullet">
/// <item>Limits come from the subscriber's plan snapshot (a metered feature's value is <c>"limit/Period"</c>).</item>
/// <item>Counting is a Redis <c>INCRBY</c> per subscriber + feature + period (<see cref="UsagePeriods"/>); a quota check
/// adds first and rolls back when over the limit, so racing requests can never both take the last unit.</item>
/// <item>The flush job persists counters to <c>usage_records</c>; a counter that Redis lost is restored from there.</item>
/// <item>Store failure FAILS OPEN: logged, counted in <c>metering.store.failures</c>, request allowed.</item>
/// <item>With <c>Subscriptions:Entitlements:Enforce = false</c> (default) usage is counted but never denied.</item>
/// </list>
/// </summary>
public sealed class UsageService(
    ISubscriberIdProvider subscribers,
    IEntitlementSnapshotSource snapshots,
    IUsageCounterStore store,
    SubscriptionsDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<SubscriptionOptions> options,
    UsageMetrics metrics,
    ILogger<UsageService> logger) : IUsageMeter, IQuotaService
{
    public async Task RecordAsync(string key, long quantity = 1, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0 || subscribers.GetCurrentSubscriberId() is not { } subscriberId)
            return;

        try
        {
            var plan = await snapshots.GetSnapshotAsync(cancellationToken);
            await IncrementAsync(subscriberId, key, QuotaOf(plan, key).Period, quantity, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.StoreFailure("record");
            logger.LogWarning(ex, "Recording usage of {Feature} failed; the usage is not counted", key);
        }
    }

    public async Task<long> GetUsageAsync(string key, CancellationToken cancellationToken = default)
    {
        if (subscribers.GetCurrentSubscriberId() is not { } subscriberId)
            return 0;

        var plan = await snapshots.GetSnapshotAsync(cancellationToken);
        return await ReadAsync(subscriberId, key, QuotaOf(plan, key).Period, cancellationToken);
    }

    public async Task<QuotaResult> TryConsumeAsync(string key, long quantity = 1, CancellationToken cancellationToken = default)
    {
        if (subscribers.GetCurrentSubscriberId() is not { } subscriberId)
            return new QuotaResult(true, 0, null);

        var enforce = options.Value.Entitlements.Enforce;
        var plan = await snapshots.GetSnapshotAsync(cancellationToken);
        var quota = QuotaOf(plan, key);

        // Enforcing: a feature the plan does not include has no quota at all.
        if (enforce && !quota.InPlan)
        {
            metrics.QuotaDenied(key);
            return new QuotaResult(false, 0, 0);
        }

        long used;
        try
        {
            used = await IncrementAsync(subscriberId, key, quota.Period, quantity, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.StoreFailure("consume");
            logger.LogWarning(ex, "Usage store unavailable: quota for {Feature} fails open", key);
            return new QuotaResult(true, 0, quota.Limit);
        }

        if (quota.Limit is { } limit && used > limit)
        {
            if (!enforce)
            {
                logger.LogDebug("Entitlements not enforced: quota {Feature} exceeded ({Used}/{Limit}) but allowed", key, used, limit);
                return new QuotaResult(true, used, limit);
            }

            try
            {
                await IncrementAsync(subscriberId, key, quota.Period, -quantity, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                metrics.StoreFailure("rollback");
                logger.LogWarning(ex, "Could not give back the unused quota of {Feature}", key);
            }

            metrics.QuotaDenied(key);
            return new QuotaResult(false, used - quantity, limit);
        }

        return new QuotaResult(true, used, quota.Limit);
    }

    /// <summary>Usage of every metered feature of the current plan (for <c>GET /subscriptions/me/usage</c>).</summary>
    public async Task<List<UsageDto>> GetPlanUsageAsync(Guid subscriberId, CancellationToken cancellationToken = default)
    {
        var result = new List<UsageDto>();
        var plan = await snapshots.GetSnapshotAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var (key, value) in plan.Features.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!FeatureValue.TryParseQuota(value, out var limit, out var period))
                continue;

            result.Add(new UsageDto(
                key,
                await ReadAsync(subscriberId, key, period, cancellationToken),
                limit,
                period.ToString(),
                UsagePeriods.KeyFor(period, now),
                UsagePeriods.ResetsAt(period, now)));
        }

        return result;
    }

    private async Task<long> IncrementAsync(
        Guid subscriberId, string key, QuotaResetPeriod period, long delta, CancellationToken ct)
    {
        var periodKey = UsagePeriods.KeyFor(period, timeProvider.GetUtcNow());
        var counter = UsagePeriods.CounterKey(subscriberId, periodKey, key);
        TimeSpan? ttl = period == QuotaResetPeriod.Monthly
            ? TimeSpan.FromDays(Math.Max(1, options.Value.Metering.MonthlyCounterTtlDays))
            : null;

        // A positive delta on a brand-new counter first seeds it with what the database last saw (Redis may have lost
        // it), atomically; a give-back (negative delta) never seeds.
        return delta > 0
            ? await store.AddSeededAsync(counter, delta, ttl, token => StoredQuantityAsync(subscriberId, key, periodKey, token), ct)
            : await store.AddAsync(counter, delta, ttl, ct);
    }

    private async Task<long> ReadAsync(Guid subscriberId, string key, QuotaResetPeriod period, CancellationToken ct)
    {
        var periodKey = UsagePeriods.KeyFor(period, timeProvider.GetUtcNow());
        try
        {
            var live = await store.GetAsync(UsagePeriods.CounterKey(subscriberId, periodKey, key), ct);
            if (live is { } value)
                return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.StoreFailure("read");
            logger.LogWarning(ex, "Usage store unavailable; reading the last persisted usage of {Feature}", key);
        }

        return await StoredQuantityAsync(subscriberId, key, periodKey, ct);
    }

    private async Task<long> StoredQuantityAsync(Guid subscriberId, string key, string periodKey, CancellationToken ct) =>
        await dbContext.UsageRecords.AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId && x.Key == key && x.PeriodKey == periodKey)
            .Select(x => x.Quantity)
            .FirstOrDefaultAsync(ct);

    private static (bool InPlan, long? Limit, QuotaResetPeriod Period) QuotaOf(EntitlementSnapshot plan, string key)
    {
        if (!plan.Features.TryGetValue(key, out var value))
            return (false, null, QuotaResetPeriod.Monthly);

        return FeatureValue.TryParseQuota(value, out var limit, out var period)
            ? (true, limit, period)
            : (FeatureValue.IsEnabled(value), null, QuotaResetPeriod.Monthly);
    }
}
