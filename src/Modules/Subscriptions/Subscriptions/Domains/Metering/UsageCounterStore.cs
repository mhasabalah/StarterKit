using Microsoft.Extensions.Options;
using Shared.Core.Options;
using StackExchange.Redis;

namespace Subscriptions.Domains.Metering;

/// <summary>
/// Fast counters for metering. Redis in production (<c>INCRBY</c> is atomic across instances); an in-process store when
/// Redis is disabled (dev/tests: single instance, lost on restart - the flush job still copies it to the database).
/// Every write also marks the counter dirty so the flush job knows what to persist.
/// </summary>
public interface IUsageCounterStore
{
    /// <summary>Atomically adds <paramref name="delta"/> (may be negative) and returns the new value.</summary>
    Task<long> AddAsync(string counterKey, long delta, TimeSpan? ttl, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="AddAsync"/> for a positive delta, but a counter that does not exist yet is first SEEDED with
    /// <paramref name="baseline"/> (what the database last saw, e.g. after Redis lost the counter) and only then
    /// incremented, as one atomic step: a racing request can never observe the un-seeded value and slip under the limit.
    /// The default implementation is the non-atomic fallback for stores that cannot do better.
    /// </summary>
    async Task<long> AddSeededAsync(
        string counterKey, long delta, TimeSpan? ttl, Func<CancellationToken, Task<long>> baseline, CancellationToken ct = default)
    {
        var value = await AddAsync(counterKey, delta, ttl, ct);
        if (value == delta && await baseline(ct) is > 0 and var seed)
            value = await AddAsync(counterKey, seed, ttl, ct);
        return value;
    }

    /// <summary>Current value, or null when the counter does not exist.</summary>
    Task<long?> GetAsync(string counterKey, CancellationToken ct = default);

    /// <summary>Removes and returns up to <paramref name="max"/> counter keys written since they were last drained.</summary>
    Task<IReadOnlyList<string>> DrainDirtyAsync(int max, CancellationToken ct = default);

    /// <summary>Puts keys back (a flush of them failed).</summary>
    Task MarkDirtyAsync(IEnumerable<string> counterKeys, CancellationToken ct = default);
}

public sealed class RedisUsageCounterStore(IConnectionMultiplexer redis, IOptions<RedisOptions> redisOptions) : IUsageCounterStore
{
    private string Prefix => redisOptions.Value.InstanceName ?? string.Empty;
    private string DirtyKey => Prefix + "usage-dirty";

    public async Task<long> AddAsync(string counterKey, long delta, TimeSpan? ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = Prefix + counterKey;
        var value = await db.StringIncrementAsync(key, delta);
        if (ttl is { } expiry)
            await db.KeyExpireAsync(key, expiry);
        await db.SetAddAsync(DirtyKey, counterKey);
        return value;
    }

    // Increments an EXISTING counter; returns nil (false) when it does not exist so the caller can seed it.
    private const string IncrementIfExistsScript =
        "if redis.call('EXISTS', KEYS[1]) == 1 then return redis.call('INCRBY', KEYS[1], ARGV[1]) else return false end";

    // Seeds a missing counter (SET only if absent) and increments, atomically.
    private const string SeedAndIncrementScript =
        "if redis.call('EXISTS', KEYS[1]) == 0 and tonumber(ARGV[2]) > 0 then redis.call('SET', KEYS[1], ARGV[2]) end " +
        "return redis.call('INCRBY', KEYS[1], ARGV[1])";

    public async Task<long> AddSeededAsync(
        string counterKey, long delta, TimeSpan? ttl, Func<CancellationToken, Task<long>> baseline, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = (RedisKey)(Prefix + counterKey);

        var existing = await db.ScriptEvaluateAsync(IncrementIfExistsScript, [key], [delta]);
        long value;
        if (!existing.IsNull)
        {
            value = (long)existing;
        }
        else
        {
            var seed = await baseline(ct);
            value = (long)await db.ScriptEvaluateAsync(SeedAndIncrementScript, [key], [delta, seed]);
        }

        if (ttl is { } expiry)
            await db.KeyExpireAsync(key, expiry);
        await db.SetAddAsync(DirtyKey, counterKey);
        return value;
    }

    public async Task<long?> GetAsync(string counterKey, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(Prefix + counterKey);
        return value.HasValue && value.TryParse(out long number) ? number : null;
    }

    public async Task<IReadOnlyList<string>> DrainDirtyAsync(int max, CancellationToken ct = default)
    {
        var popped = await redis.GetDatabase().SetPopAsync(DirtyKey, max);
        return popped.Where(x => x.HasValue).Select(x => (string)x!).ToList();
    }

    public async Task MarkDirtyAsync(IEnumerable<string> counterKeys, CancellationToken ct = default)
    {
        var values = counterKeys.Select(x => (RedisValue)x).ToArray();
        if (values.Length > 0)
            await redis.GetDatabase().SetAddAsync(DirtyKey, values);
    }
}

public sealed class InMemoryUsageCounterStore : IUsageCounterStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _counters = [];
    private readonly HashSet<string> _dirty = [];

    public Task<long> AddAsync(string counterKey, long delta, TimeSpan? ttl, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var value = _counters.GetValueOrDefault(counterKey) + delta;
            _counters[counterKey] = value;
            _dirty.Add(counterKey);
            return Task.FromResult(value);
        }
    }

    public async Task<long> AddSeededAsync(
        string counterKey, long delta, TimeSpan? ttl, Func<CancellationToken, Task<long>> baseline, CancellationToken ct = default)
    {
        bool missing;
        lock (_gate)
            missing = !_counters.ContainsKey(counterKey);

        var seed = missing ? await baseline(ct) : 0;
        lock (_gate)
        {
            // Seed-if-absent and add under one lock: whoever gets here first seeds, everyone adds on top.
            if (!_counters.ContainsKey(counterKey) && seed > 0)
                _counters[counterKey] = seed;
            var value = _counters.GetValueOrDefault(counterKey) + delta;
            _counters[counterKey] = value;
            _dirty.Add(counterKey);
            return value;
        }
    }

    public Task<long?> GetAsync(string counterKey, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_counters.TryGetValue(counterKey, out var value) ? (long?)value : null);
    }

    public Task<IReadOnlyList<string>> DrainDirtyAsync(int max, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var batch = _dirty.Take(max).ToList();
            _dirty.ExceptWith(batch);
            return Task.FromResult<IReadOnlyList<string>>(batch);
        }
    }

    public Task MarkDirtyAsync(IEnumerable<string> counterKeys, CancellationToken ct = default)
    {
        lock (_gate)
            _dirty.UnionWith(counterKeys);
        return Task.CompletedTask;
    }
}
