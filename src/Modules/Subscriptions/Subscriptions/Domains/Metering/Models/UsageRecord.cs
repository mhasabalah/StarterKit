namespace Subscriptions.Domains.Metering.Models;

/// <summary>
/// Durable copy of one subscriber's usage of one metered feature in one period. The live counter is in Redis; the
/// flush job writes its absolute value here (an upsert, so it is idempotent) and the record is the fallback when
/// Redis lost the counter.
/// </summary>
public class UsageRecord
{
    public Guid Id { get; private set; }
    public Guid SubscriberId { get; private set; }
    public string Key { get; private set; } = default!;

    /// <summary><c>2026-11</c> for monthly counters, <c>all</c> for never-resetting ones.</summary>
    public string PeriodKey { get; private set; } = default!;

    public long Quantity { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private UsageRecord()
    {
    }

    public static UsageRecord Create(Guid subscriberId, string key, string periodKey, long quantity, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            SubscriberId = subscriberId,
            Key = key,
            PeriodKey = periodKey,
            Quantity = quantity,
            UpdatedAt = now
        };

    /// <summary>Sets the absolute quantity (never lowers it: a stale flush must not lose usage).</summary>
    public void Set(long quantity, DateTimeOffset now)
    {
        Quantity = Math.Max(Quantity, quantity);
        UpdatedAt = now;
    }
}
