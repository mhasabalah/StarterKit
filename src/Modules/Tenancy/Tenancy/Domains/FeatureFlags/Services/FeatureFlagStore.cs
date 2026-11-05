using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shared.Core.FeatureFlags;
using Shared.Core.MultiTenancy.Caching;
using Tenancy.Domains.FeatureFlags.Models;

namespace Tenancy.Domains.FeatureFlags.Services;

/// <summary>
/// Read side of the stored flags: ONE snapshot of every flag with its overrides, cached in-process
/// (<c>MultiTenancy:Cache:LocalSeconds</c>) over distributed/Redis (<c>DistributedSeconds</c>), like
/// <see cref="Tenancy.Services.TenantStore"/>. <see cref="InvalidateAsync"/> (run after every flag change, from
/// <c>FeatureFlagChangedEventHandler</c>) drops both levels and broadcasts the L1 eviction over
/// <see cref="ILocalCacheInvalidationBus"/> so the other replicas stop serving the old value at once; without Redis they
/// expire within LocalSeconds.
/// <para>
/// Failure policy: a cache failure never fails the request (the database is read instead); if the database itself
/// cannot be read the snapshot is empty and NOT cached, so evaluation falls back to <c>FeatureFlags:Defaults</c>.
/// </para>
/// </summary>
public sealed class FeatureFlagStore(
    IUnitOfWorkRunner runner,
    IMemoryCache memoryCache,
    IDistributedCache distributedCache,
    IOptions<MultiTenancyOptions> options,
    ILocalCacheInvalidationBus invalidationBus,
    ILogger<FeatureFlagStore> logger)
{
    public const string CacheKey = "tenancy:flags:snapshot";

    /// <summary>
    /// Bumped by every <see cref="InvalidateAsync"/>. A reader remembers it BEFORE loading from the database and only
    /// caches what it loaded if it is unchanged afterwards, so a snapshot read before a flag change can never be
    /// written back after that change's invalidation.
    /// </summary>
    public const string GenerationKey = "tenancy:flags:generation";

    private long _localGeneration;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, FeatureFlagDefinition>> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        if (memoryCache.TryGetValue(CacheKey, out Snapshot? local) && local is not null)
            return local.ToDictionary();

        var cached = await ReadDistributedAsync(cancellationToken);
        if (cached is not null)
        {
            SetLocal(cached);
            return cached.ToDictionary();
        }

        var generation = await ReadGenerationAsync(cancellationToken);
        var localGeneration = Interlocked.Read(ref _localGeneration);

        Snapshot loaded;
        try
        {
            loaded = await runner.RunAsync(async (services, ct) =>
            {
                var flags = await services.GetRequiredService<TenancyDbContext>().FeatureFlags.AsNoTracking()
                    .Include(f => f.Overrides)
                    .ToListAsync(ct);
                return new Snapshot(flags.Select(Map).ToList());
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reading the feature flags failed; serving the configured defaults");
            return new Dictionary<string, FeatureFlagDefinition>();
        }

        // Invalidated while loading: this snapshot may predate the change, so serve it once but do not cache it.
        if (Interlocked.Read(ref _localGeneration) == localGeneration &&
            await ReadGenerationAsync(cancellationToken) == generation)
        {
            SetLocal(loaded);
            await WriteDistributedAsync(loaded, cancellationToken);
        }

        return loaded.ToDictionary();
    }

    /// <summary>Drops the snapshot on this instance (both levels) and tells the other instances to drop their L1 copy.</summary>
    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _localGeneration);
        memoryCache.Remove(CacheKey);
        try
        {
            // Generation first, snapshot second: a reader that loaded before this point sees the new generation.
            await distributedCache.SetStringAsync(GenerationKey, Guid.NewGuid().ToString("N"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) },
                cancellationToken);
            await distributedCache.RemoveAsync(CacheKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not invalidate the cached feature flags");
        }

        // Broadcast LAST: peers evict L1 on receipt and re-read L2, which must already be gone.
        await invalidationBus.PublishAsync([CacheKey], cancellationToken);
    }

    private static FeatureFlagDefinition Map(FeatureFlag flag) => new(
        flag.Key, flag.Enabled, flag.RolloutPercent, flag.KillSwitch,
        flag.Overrides.Where(o => o.TenantId is not null).ToDictionary(o => o.TenantId!.Value, o => o.Enabled),
        flag.Overrides.Where(o => o.UserId is not null).ToDictionary(o => o.UserId!.Value, o => o.Enabled));

    private void SetLocal(Snapshot snapshot)
    {
        var seconds = options.Value.Cache.LocalSeconds;
        if (seconds > 0)
            memoryCache.Set(CacheKey, snapshot, TimeSpan.FromSeconds(seconds));
    }

    private async Task<string?> ReadGenerationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await distributedCache.GetStringAsync(GenerationKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading the feature flag cache generation failed");
            return null;
        }
    }

    private async Task<Snapshot?> ReadDistributedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await distributedCache.GetStringAsync(CacheKey, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<Snapshot>(json, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading the feature flag cache failed; using the database");
            return null;
        }
    }

    private async Task WriteDistributedAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        var seconds = options.Value.Cache.DistributedSeconds;
        if (seconds <= 0) return;

        try
        {
            await distributedCache.SetStringAsync(CacheKey, JsonSerializer.Serialize(snapshot, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(seconds) },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Writing the feature flag cache failed");
        }
    }

    /// <summary>The cached shape (a list, because a dictionary with case-insensitive keys does not round-trip JSON).</summary>
    private sealed record Snapshot(List<FeatureFlagDefinition> Flags)
    {
        public Dictionary<string, FeatureFlagDefinition> ToDictionary() =>
            Flags.ToDictionary(f => f.Key, StringComparer.Ordinal);
    }
}
